---
title: PTY の起動、制御、出力取得
description: 端末として振る舞う子 process を起動し、入力転送、出力の集約、終了処理までを扱う仕組み。
---

標準出力を pipe へリダイレクトしただけでは、端末上の実行と同じ挙動にはなりません。
program は TTY の有無によって色、buffering、進捗表示、全画面 UI を切り替えることがあります。
console2svg は可能な場合に **PTY** を使い、子 process から端末へ接続されているように見える入出力を用意します。

## OS 固有の PTY をライブラリ境界へ寄せる

現在の録画層は `Porta.Pty` を使って platform ごとの PTY を起動、管理します。
console2svg 側は各 OS の PTY API を独自実装せず、process 設定、stream、端末寸法、環境変数、入力転送、時刻記録、終了処理を backend の周囲で管理します。

Windows では pseudoconsole の入出力と同様に、文字データと VT control sequence が byte stream を流れます。
Unix 系では controller 側と terminal 側を持つ通常の PTY model を使います。

子 process へは指定した列数と行数を渡し、`COLUMNS` と `LINES` にも同じ値を設定します。
command は Windows では `cmd.exe`、Unix 系では `/bin/sh` を介して実行します。

Windows の process 起動は最終的に一つの command-line 表現を扱うため、引数を事前に quote します。
`cmd.exe /c` の payload は通常の C runtime 引数と quote 規則が異なるため、別の処理を使います。

既定では `CI` と `TF_BUILD` など一部の CI 環境変数を子 shell から外します。
TTY 上でも CI 判定だけで色や対話表示を無効にする library があるためです。
親環境をそのまま渡したい場合は、この削除を無効にできます。

## 文字境界を保ったまま出力を読む

PTY の出力 reader は、read loop 全体で一つの byte buffer、一つの char buffer、一つの stateful decoder を使います。
UTF-8 の一文字が OS の read 境界で二つに分かれても、次回 read へ decoder state を引き継げます。

byte stream へ画面出力をミラーする場合は、受信した byte をそのまま転送します。
一度文字列へ decode してから再 encode しないため、VT sequence を forwarding 側で変形させません。
Windows の text forwarding では、必要な場合に console output encoding を一時的に UTF-8 へ合わせます。

録画用に decode した文字列は、経過時間と対にして `RecordingSession` へ入れます。
ここでは改行単位に分割しません。
carriage return、cursor move、erase、alternate screen の操作も、後段の端末エミュレーターが解釈する入力だからです。

## 小さな出力をまとめて録画する

一回の画面更新が、多数の小さい PTY read に分かれることがあります。
read ごとにイベントを作ると、後段の ANSI parser と animation reducer が、画面上の意味を持たない read 境界まで処理することになります。

console2svg は近接する出力を **出力コアレッシング** でまとめます。
既定の窓幅は動画の一フレーム時間の4分の1で、2ミリ秒から20ミリ秒の範囲へ制限します。
一つの batch が一フレーム時間を超えて伸び続けることも防ぎます。

明示的な設定で窓幅を変更したり、コアレッシングを無効にしたりできます。
まとめたイベントには、その batch の最後の chunk の時刻を記録します。

## 対話入力を raw な byte として転送する

対話キャプチャでは、host 側の入力を **raw 入力** に近い状態へ切り替えます。
矢印キーや Ctrl 系の入力を host が先に処理せず、子 process へ VT sequence として転送するためです。

Unix 系では標準入力がリダイレクトされていても、`/dev/tty` を開ける場合は対話入力へ利用します。

入力を replay として同時保存するときは、転送する元 byte を先に PTY へ書き、その byte を UTF-8 decoder で解釈します。
VT sequence は ASCII で構成されるため、legacy console code page で ESC を別の byte sequence の一部として扱うことを避けます。

read の末尾で CSI などの escape sequence が途中までしか届いていない場合は、その残りを次の read へ持ち越します。
stream の read 境界だけを理由に、未完了の sequence を別の key event へ変換しません。

## echo された control byte を録画へ混ぜない

host input を PTY へ書くと、slave 側の echo 設定によって同じ byte が output として戻ることがあります。
Unix 系では `ECHOCTL` によって ESC などの control character が caret notation へ変換される場合もあります。

live forwarding では、console2svg が PTY controller stream 経由で slave の echo 関連 flag を無効化する処理を試みます。
backend と OS の条件に依存するため、この操作は best effort です。
replay input では host keyboard の echo を抑える同じ処理は必要ありません。

キャプチャ終了時には、full-screen application が有効化した mouse tracking mode も解除します。
録画データを書き換える処理ではなく、利用者の terminal session を復元するための後始末です。

## process 終了後の残り出力を回収する

子 process の終了と、既に PTY へ書かれた byte の読み取り完了は同時とは限りません。
process 終了後も最大500ミリ秒だけ output reader を動かし、kernel buffer に残る出力を回収します。

PTY close の見え方は platform ごとに同じではありません。
teardown に伴う既知の I/O error は EOF 相当として扱い、既に取得した録画を確定できるようにします。

cleanup にも上限があります。
connection の dispose と output reader の停止は、それぞれ1秒を超えて待ち続けません。
backend の終了処理が停止しても CLI 全体を無期限に止めないためです。

## PTY を利用できない場合のフォールバック

PTY backend が起動しても一定時間出力を生成しない場合は、startup hang として再試行します。
最大3回試し、試行間には短い待機を入れます。

native backend を load できない場合や、再試行しても起動できない場合は、redirected stream を使う通常 process へフォールバックします。
この経路では TTY 依存の挙動を完全には再現できません。
一方で、PTY を使えない環境でも非対話 command の録画まで無期限停止させずに済みます。
