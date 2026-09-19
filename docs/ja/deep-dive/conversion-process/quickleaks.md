---
title: QuickLeaks による自動マスク
description: Betterleaks 由来の検出結果を絞り込み、端末セルへ戻して SVG から秘密文字列を除く仕組み。
---

自動マスクは、端末の文字列を SVG へ書き出す前に検出を行います。
検出器は UTF-16 上の範囲を返し、renderer はその範囲がどの terminal cell に対応するかを決めます。
文字列検出と cell geometry を分けることで、検出器を通常の文字列処理として保ちながら、描画時には端末の列位置を維持できます。

## QuickLeaks と Betterleaks の担当範囲を分ける

**QuickLeaks** は、固定した Betterleaks の rule set と console2svg 固有 rule から生成する .NET 内蔵 detector です。
現在の生成済み source には465 rule が含まれます。

Betterleaks の rule ID、keyword、regular expression は取り込みますが、Betterleaks の実行機構をすべて含めるわけではありません。
expression filter、validator、provider または network check、repository context を使う処理は意図的に含めません。

そのため、QuickLeaks の finding は「秘密情報らしい pattern に一致した」ことを表します。
実際に有効な credential であることや、provider で利用できる値であることまでは確認しません。

rule を C# source として生成しておくため、実行時に外部 scanner process や rule 設定 file を読み込む必要もありません。

## compiler が証明した anchor を一度だけ検索する

画面ごとに数百個の regular expression をすべて評価すると、特に animation の自動マスクで負荷が増えます。

生成器は regex を保守的に解析し、match 内に必ず現れると証明できた固定 anchor を抽出します。
解析できない構文は最適化せず、regex fallback に残します。rule を削除することはありません。

実行時は .NET の `SearchValues<string>` で anchor を一括検索します。
見つかった位置から生成済み discriminator が exact anchor と rule index を特定し、compact な bitset に追加します。
候補処理は全 rule を順番に確認せず、立っている bit だけを列挙します。

`ghp_[0-9A-Za-z]{36}` のような単純な prefix-token 形状は専用 verifier
へコンパイルします。anchor が見つかった位置の後ろだけを読み、case、長さ、
ASCII character class、word boundary を検証します。現在の pinned rule では
39ルールが regex を実行せず、この経路を使います。さらに、provider assignment
の共通形状74ルールと、home directory の固定レイアウトを専用 verifier へ移しています。

Betterleaks keyword は compiler-proven anchor とは別のものです。
専用 verifier への移行中は、従来と同等以上の recall を保つ safety net として keyword も併用します。

fallback regex は `GeneratedRegex` と span-based `Regex.EnumerateMatches` を使い、match timeout も固定します。
一つの pathological な入力によって、一つの rule が無制限に評価を続けないようにします。
timeout 時は finding を黙って捨てず、安全側の conservative redaction を返します。

prefix-token や credential URI のように専用 verifier へ lowering 済みの rule は
regex を実行しません。generation report には選択した engine と、残りの rule の
fallback 理由を記録し、段階的に verifier へ移せます。

安全に上限を付けられる fallback は `RegexOptions.NonBacktracking` を使います。
大きな automata や未対応構文だけが finite-timeout backtracking に残ります。
terminal 正規化は再利用可能な文字バッファへ書き込み、`ToString()` を介さず span を渡します。

## 入力途中だけ Early モードで量指定子を緩める

入力中の値を完成前から覆う用途には **Early モード** を使えます。

生成器は固定長の量指定子を無差別に緩めません。
rule の secret-value capture の内側だけを対象にし、32文字必要な token なら入力途中の短い prefix も一致できる形へ変換します。
secret capture の外側にある文脈用の量指定子は維持します。

Early モードは通常モードより誤検出が増えやすいため、確定済み出力の既定動作とは分けています。

## 一致範囲から読解に必要な接頭辞を残す

regex が返した範囲全体を、そのまま消すとは限りません。

一般的な `key=value` 形式では value だけへ範囲を狭め、key を残します。
credential URI では username と password を別々に mask し、scheme、separator、host を残せます。
home directory rule では directory prefix を残し、利用者固有の部分へ範囲を狭めます。
Git identity rule では display name と email local part を別々に mask し、email domain を残します。

これにより、何の出力だったかを判断するための構造を残しながら、検出した値そのものを SVG へ残さないようにします。

## 検出文字列を terminal cell へ戻す

renderer は可視領域を一つの正規化文字列へ変換します。
全角文字の continuation cell を考慮し、行末の空 cell を取り除き、物理行が terminal wrap の続きである場合は途中へ改行を挿入しません。
画面上では二行に分かれた token でも、通常の折返しなら detector 上では連続した文字列として扱えます。

自動マスクの通常経路では **二段階マッピング** を使います。
最初の pass は正規化文字列だけを作り、QuickLeaks を実行します。
finding が一件もなければ、文字ごとの cell 座標 List を作りません。

finding が存在する場合だけ、正規化文字列をもう一度作りながら、各文字の元になった `(row, column)` を記録します。
finding の UTF-16 範囲を、その座標表から terminal cell 集合へ変換します。

秘密情報を含まない画面が多数続く場合に、全文字分の座標 object を毎回割り当てないための処理です。

## SVG の text node から検出値を除く

mask 対象 cell は、元の文字を opaque な矩形の下へ残しません。
描画文字を `*` へ置き換えた上で、連続する mask cell へ stripe overlay を重ねます。

SVG は source を直接読める形式です。
overlay だけで隠すと、見た目では読めなくても元の文字列を検索、copy、source inspection から取得できるためです。

置換後も cell 数は変えません。
後続文字の列位置は元の端末と同じままです。

## アニメーション最適化とマスクの境界を守る

自動マスクと手動マスクの scan は、foreground を描く pass だけで実行します。
background だけの pass では露出する文字列がないため、検出用の一時データを作りません。

`FrameRenderWorkspace` が正規化文字列用の `StringBuilder` を保持し、行定義を繰り返し描くときに backing buffer を再利用します。

手動 mask pattern がある場合は行差分を無効にします。
literal pattern が変更していない基底部分と差分部分をまたぐ可能性があり、行を分割すると完全な文字列を matcher へ渡せないためです。
