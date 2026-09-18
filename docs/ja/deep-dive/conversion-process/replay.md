---
title: 入力リプレイの記録と再生
description: 対話入力を時刻付き key event へ正規化し、再生時に VT byte sequence へ戻して PTY へ送る仕組み。
---

replay が保存するのは terminal output ではなく、利用者が行った入力操作です。
再生時は同じ command を PTY で起動し、記録した操作を予定時刻に送って、その結果として生じた terminal output を改めて録画します。

そのため replay file は Windows 固有の console event や Unix 固有の入力構造ではなく、platform をまたいで扱える key の意味を保存します。

## replay file の時刻を正規化する

最初の input event は、録画開始からの絶対秒を `time` として持てます。
後続 event は直前からの差分を `tick` として持てます。

読み込み時は、`time` がある event ではその値を優先します。
`tick` だけを持つ event は前の時刻へ加算し、絶対時刻の timeline へ正規化します。

metadata には format と application の情報に加えて total duration も含めます。
最後の key event の時刻だけでは、録画を終了すべき時点を表せないためです。

input をすべて送り終えても、子 process が prompt で待ち続けることがあります。
replay は total duration に1秒を加えた時点を上限とし、それを超えた場合は timeout として扱います。

## live input の途中で VT sequence を壊さない

対話入力を replay として同時保存する場合も、forwarding 経路は元の byte を先に PTY へ書きます。

replay model へ変換する部分では UTF-8 decoder を使います。
VT key sequence は ASCII であり、Windows の legacy code page に ESC を解釈させると sequence byte を別の文字 encoding の一部として扱う可能性があるためです。

stream read は CSI、SS3、OSC、DCS、APC、PM、SOS の途中で切れることがあります。
末尾に未完了の escape sequence があれば、次の input chunk へ持ち越します。
read が終わった位置だけを理由に、不完全な prefix を別の key event へ変換しません。

terminal protocol の control string は、利用者が再現したい key input とは限りません。
そのため replay event として保存しない対象があります。

## OS 固有 key code ではなく意味を保存する

一般的な key は `ArrowUp`、`Home`、`Delete`、`F1` から `F12` のような名前へ正規化します。
Shift、Alt、Ctrl、Meta は modifier として別に持ちます。

printable Unicode は platform key code ではなく文字列として保存します。
surrogate pair も一つの logical key value として扱います。

通常の key model へ収まらない入力には `raw` event を使えます。
この場合は保持した byte-oriented な値を再生側へ渡せます。

## replay event を VT byte へ戻す

再生時には key name と modifier を、terminal application が受け取る VT sequence へ変換します。

Enter、Tab、矢印、navigation key、function key は対応する escape sequence を使います。
Ctrl と alphabet の組合せは control byte へ変換します。
Alt は必要に応じて ESC prefix を使い、printable text は UTF-8 へ encode します。

生成した byte は、live input と同じ PTY writer へ送ります。
keyboard 入力と replay 入力で、output capture の terminal 経路を分けません。

## event 順序を変えずに予定時刻へ送る

replay stream は event の byte 表現を用意し、read された時点で対象 event の予定時刻まで待ちます。

既に予定時刻より処理が遅れている場合は、追加の待機を入れません。
元の順序を維持したまま次の event へ追いつきます。

一つの event が consumer の read buffer より大きい場合もあります。
stream は現在 event 内の offset を保持し、複数回の read に分けて返します。
stream boundary で分割されても入力の残りを捨てません。
