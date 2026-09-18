---
title: 端末制御シーケンスの解釈
description: 分割された VT 出力を状態付きで読み、cursor、セル、属性、scroll、alternate screen へ反映する仕組み。
---

端末出力は装飾付き文字列ではなく、画面状態を変更する命令列です。
carriage return は既存行を上書きでき、CSI は文字を出さずに cursor を移動でき、full-screen application は alternate screen へ切り替えて一部分だけを再描画できます。
escape sequence を削除して文字だけ残すと、最終画面を復元するための操作情報を失います。

console2svg は SVG を作る前に、これらの命令を **状態付きパース** して `ScreenBuffer` へ反映します。

## read 境界をシーケンス境界として扱わない

OS の read は ESC、CSI、OSC などの途中で終わることがあります。
`AnsiParser` は未完了の sequence を保持し、次の `Process` 呼び出しで続きを解釈します。

Unix の echo 設定によって、ESC が `^[` のような caret notation として見える場合もあります。
この表現で届く OSC には別の pending state を持ちます。
通常の `^[` 文字列まで広く control sequence として消費しないよう、対象を限定しています。

OSC と DCS の payload は printable cell ではないため、terminator まで読み飛ばします。
G0 と G1 の character set 指定、SO と SI による選択も parser state として保持します。
DEC special graphics は、その state に基づいて対応する罫線文字へ変換します。

## CSI の parameter を文字列分割しない

CSI の parameter は span 上で読みます。
`string.Split` で parameter ごとの文字列や配列を作りません。

parameter 数を先に数え、16個以下なら `stackalloc` した integer span を使います。
17個以上の場合だけ `ArrayPool<int>` から配列を借り、処理後に返します。

一般的な SGR と cursor control は parameter 数が少ないため、sequence ごとの heap allocation を避けられます。

private marker は parameter と分けて扱います。
未対応の private sequence は別の標準命令として誤解釈せず、無視します。

## 画面操作をセル状態へ適用する

実装する CSI には、cursor の相対移動と絶対移動、画面消去、行消去、文字挿入と削除、行挿入と削除、scroll、scroll region、tab 制御、insert mode、repeat、save、restore が含まれます。

DEC private mode では alternate screen、origin mode、cursor visibility を扱います。
alternate screen は main screen と別の cell buffer です。
TUI が終了したときは、TUI のセルを shell 履歴へ混ぜずに元の main screen へ戻せます。

cursor の save と restore では、その後の文字配置に影響する terminal state も保持します。
制御 sequence を受け取った瞬間の見た目だけでなく、その sequence が後続出力へ与える効果まで再現するためです。

## SGR をセルの表示スタイルへ解決する

SGR は `TextStyle` を更新します。
保持する属性には bold、faint、italic、underline、blink、reverse、hidden、strikethrough、overline、foreground、background、underline color があります。

色は通常の16色、xterm 256色、true RGB を扱います。
256色は active theme の先頭16色、6×6×6 color cube、grayscale へ解決します。
extended color は semicolon 区切りと colon 区切りの両方を同じ style state へ正規化します。

`ScreenBuffer` は同じ style を共有するため、隣接セルごとに別の style object を作りません。
同じ SGR 状態で文字が続く通常ケースでは、直前の `CellStyle` を再利用します。

## Unicode を terminal cell に合わせる

UTF-16 の一 code unit と terminal の一 cell は同じ単位ではありません。
surrogate pair は配置前に一つの文字 cluster として扱います。
combining mark と variation selector は直前セルへ追加し、zero-width character は cursor 列を進めません。

全角文字は二列を占有します。
先頭セルへ文字列を置き、次のセルは continuation として記録します。
上書きや行差分でも continuation を考慮し、後段の SVG text run が一列ずれないようにします。

variation selector 16 によって、直前の記号を wide emoji 表示へ変える場合もあります。
必要な幅調整は SVG 化より前の cell model で行います。

## ScreenBuffer を後段の共通入力にする

パース後の renderer は、生の CSI syntax を再解釈しません。
解決済みの文字列、style、幅情報、cursor、active screen、scroll state を持つ **`ScreenBuffer`** を入力にします。

同じ buffer は、animation と video sampling で使う visual signature と copy-on-write の行共有も持ちます。
VT の意味解釈を一度この層で終え、SVG、PNG、動画は同じ端末状態を利用します。
