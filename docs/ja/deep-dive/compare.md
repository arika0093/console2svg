---
title: 類似ツールとの比較
description: console2svg 相当の機能を持つツールとの比較
---

ターミナル出力の記録、画像化、共有、自動化は既存のツールを組み合わせても実現できます。
この文書では、`console2svg` の主要機能に対応する代替ツールを取り上げ、できること、得意な領域、導入依存の観点などから対比します。

> [!NOTE]
> 網羅的な星取り表でなく、代表的なツールの機能比較を目的としています。

## 単発的なターミナル画像の生成

[`capture`](../basic-usage/capturing-images/overview.mdx) に相当する機能です。CLI コマンドを実行し、その出力結果を静止画として保存します。

| ツール | 主な出力形式 | 主な機能・特徴 | 外部依存・実行環境 | リポジトリ指標 |
| :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | SVG（PNG/動画も可） | コマンド指定による直接 SVG 生成、装飾・テーマ・自動マスキング | 配布アーカイブ（resvg ネイティブライブラリ同梱、Windows は ffmpeg 同梱） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**asciinema**](https://github.com/asciinema/asciinema) + 変換ツール | asciicast（変換で SVG/GIF） | 端末イベントの軽量記録、Web 再生、後段ツール連携 | Python/Rust（本体）+ svg-term-cli（Node.js）など | [![GitHub last commit](https://img.shields.io/github/last-commit/asciinema/asciinema)](https://github.com/asciinema/asciinema) [![GitHub stars](https://img.shields.io/github/stars/asciinema/asciinema)](https://github.com/asciinema/asciinema) |
| [**termtosvg**](https://github.com/nbedos/termtosvg) | SVG（アニメーション/静止画） | テンプレートによる SVG 装飾、asciicast からのレンダリング | Python 3、pyte、lxml | アーカイブ済み（2020年） [![GitHub stars](https://img.shields.io/github/stars/nbedos/termtosvg)](https://github.com/nbedos/termtosvg) |
| [**tmux (`capture-pane`)**](https://github.com/tmux/tmux) | プレーンテキスト | 既存ペインやスクロールバック履歴のバッファ抽出 | tmux（Unix 系環境、C 言語） | [![GitHub last commit](https://img.shields.io/github/last-commit/tmux/tmux)](https://github.com/tmux/tmux) [![GitHub stars](https://img.shields.io/github/stars/tmux/tmux)](https://github.com/tmux/tmux) |
| [**termshot**](https://github.com/homeport/termshot) | PNG | コマンド実行からの即時 PNG スクリーンショット生成 | Go 製バイナリ、macOS/Linux 主体 | [![GitHub last commit](https://img.shields.io/github/last-commit/homeport/termshot)](https://github.com/homeport/termshot) [![GitHub stars](https://img.shields.io/github/stars/homeport/termshot)](https://github.com/homeport/termshot) |

### asciinema + SVG/GIF 変換ツール

[asciinema](https://docs.asciinema.org/) はターミナルの入出力を時間情報とともに asciicast 形式（`.cast`）へ記録するツールです。テキストとタイミングのみを保持するため記録ファイルが小さく、Web プレイヤー上でテキスト選択やコピーが可能です。

asciinema 単体では SVG などの画像ファイルを直接出力しません。静止画やアニメーション SVG を得るには [svg-term-cli](https://github.com/marionebl/svg-term-cli) や [scenetake](https://github.com/guitarrapc/scenetake) などの外部変換ツールを、GIF を得るには [agg](https://docs.asciinema.org/manual/agg/) を組み合わせて利用します。

### termtosvg

[termtosvg](https://github.com/nbedos/termtosvg) は、ターミナルセッションを実行して SVG アニメーションや静止フレームを生成する Python 製ツールです。SVG テンプレート機構を備えており、ウィンドウ装飾やフォントのカスタマイズが可能です。

公式リポジトリは 2020 年にアーカイブされており、メンテナンスが終了しています。実行には Python 実行環境および依存ライブラリ（pyte、lxml）が必要です。

### tmux

[tmux](https://man7.org/linux/man-pages/man1/tmux.1.html) はターミナルマルチプレクサであり、`capture-pane` コマンドによって実行中ペインの表示内容やスクロールバック履歴をプレーンテキスト（エスケープシーケンス含む）としてバッファに取得できます。

tmux 自体には画像生成機能はありません。画像ファイルを出力するには取得したテキストを別の描画ツールへ渡す必要があります。

### termshot

[termshot](https://github.com/homeport/termshot) は、コマンドの ANSI 出力を解析してウィンドウ装飾付きの PNG 画像を生成する Go 製の CLI ツールです。

出力はラスター画像（PNG）であり、ベクター画像（SVG）の出力には対応していません。また、macOS および Linux を主に対象としています。

### console2svg

`console2svg capture` は、コマンドの実行から端末エミュレーション、SVG 画像の生成までを実行します。ウィンドウ装飾（macOS 風や Windows 風など）、テーマ適用、シークレット自動マスキングをコマンドライン引数で設定できます。PNG 変換用のネイティブライブラリ（resvg）や、Windows リリースでは動画生成用の ffmpeg もアーカイブ内に同梱して配布されています。

一方、生成された静止画 SVG は画像ファイルであるため、asciinema の Web プレイヤーのように再生中にテキストを選択・コピーしたり、再生速度を自由に変更したりする機能は持ちません。また、複雑な画面描画を行うコマンドでは端末エミュレーターの実装差による表示ズレが生じる場合があります。

## 決められたシナリオから画像を生成する

[`replay`](../automation/replay.md) および `scenario` に相当する機能です。手順をあらかじめスクリプトや定義ファイルに記述し、CI やドキュメントビルド時に再現可能なデモ画像・動画を生成します。

| ツール | シナリオ定義形式 | 主な出力形式 | 主な機能・特徴 | 外部依存・実行環境 | リポジトリ指標 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | JSON（キー入力記録）または Scenario（YAML/JSON） | SVG、GIF、MP4、WebM | PTY へのキーストローク再送、検証（Verify）ステップ、SVG 装飾の一括適用 | 配布アーカイブ（動画変換に ffmpeg） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**scenetake**](https://github.com/guitarrapc/scenetake) | YAML | asciicast v3、アニメーション SVG | コマンド列の宣言的記述、`pty: true` による実時間記録、ハイライト指定 | .NET ツール / npm / 各種バイナリ | [![GitHub last commit](https://img.shields.io/github/last-commit/guitarrapc/scenetake)](https://github.com/guitarrapc/scenetake) [![GitHub stars](https://img.shields.io/github/stars/guitarrapc/scenetake)](https://github.com/guitarrapc/scenetake) |
| [**Charmbracelet VHS**](https://github.com/charmbracelet/vhs) | Tape（独自スクリプト言語） | GIF、MP4、WebM、PNG（スクリーンショット） | タイピング風アニメーション、豊富なテーマ、テスト自動化 | Go 製バイナリ + ttyd + ffmpeg | [![GitHub last commit](https://img.shields.io/github/last-commit/charmbracelet/vhs)](https://github.com/charmbracelet/vhs) [![GitHub stars](https://img.shields.io/github/stars/charmbracelet/vhs)](https://github.com/charmbracelet/vhs) |

### guitarrapc/scenetake

[scenetake](https://github.com/guitarrapc/scenetake) は、YAML ファイルに実行したいコマンド列を宣言的に定義し、実行結果から asciicast v3 やアニメーション SVG を生成するツールです。行ごとのハイライトや入力速度の揺らぎ（jitter）を設定でき、`pty: true` を指定したステップでは擬似端末を介したストリーム記録を行えます。

コマンド一覧を YAML で定義し、出力ログを自動収集してドキュメント用デモを作成する用途に対応します。

### Charmbracelet VHS

[VHS](https://github.com/charmbracelet/vhs) は、`.tape` という独自のテキストファイルにキー入力や待機時間を記述し、ターミナル操作のデモ動画（GIF/MP4/WebM）をコードとして生成するツールです。

タイピング演出を含む動画や GIF の自動生成に対応しています。実行には内部で Web 端末サーバーの `ttyd` と、動画エンコード用の `ffmpeg` を必要とします。SVG ベクター画像は出力できません。

### console2svg

`console2svg replay` はキーストロークと時間間隔を記録した JSON を PTY へ再送し、対話操作の画面を再現します。また `console2svg scenario` は YAML/JSON で記述されたライフサイクル（準備、実行、検証、後処理）に従ってテストを実行し、SVG や動画を出力します。

一方で、VHS のような人間らしいタイピング揺らぎ演出や、scenetake のような行単位ハイライト構文を直接 YAML 内で装飾指定する機能は備えていません。

## 対話操作中のキャプチャ

[`interactive`](../basic-usage/interactive-capture.md) に相当する機能です。シェルや TUI を手動で操作しながら、任意の瞬間を撮影・録画します。

| ツール | 操作方法 | 主な出力形式 | 主な機能・特徴 | 外部依存・実行環境 | リポジトリ指標 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | PTY 内で作業し、ショートカットキー（F9/F10）で撮影 | SVG（静止画）、GIF/MP4（動画） | 作業を中断せずその場でベクター静止画・動画を切り出し保存 | 配布アーカイブ（動画変換に ffmpeg） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**asciinema**](https://github.com/asciinema/asciinema) | `rec` で開始し、シェル終了で保存 | asciicast | セッション全体の完全記録、後からの再生・編集 | 単一バイナリ（v3） | [![GitHub last commit](https://img.shields.io/github/last-commit/asciinema/asciinema)](https://github.com/asciinema/asciinema) [![GitHub stars](https://img.shields.io/github/stars/asciinema/asciinema)](https://github.com/asciinema/asciinema) |
| [**Terminalizer**](https://github.com/faressoft/terminalizer) | `record` で開始し、YAML 編集後にレンダリング | GIF、Web プレイヤー | 記録フレームの YAML 編集、Web プレイヤー書き出し | Node.js、C++ ビルドツール（node-gyp） | [![GitHub last commit](https://img.shields.io/github/last-commit/faressoft/terminalizer)](https://github.com/faressoft/terminalizer) [![GitHub stars](https://img.shields.io/github/stars/faressoft/terminalizer)](https://github.com/faressoft/terminalizer) |

### asciinema

[asciinema](https://docs.asciinema.org/manual/cli/) はセッションの開始から終了までを 1 本のストリームとして記録します。静止画が必要な場合、セッション全体を記録した後に外部ツール等で該当フレームを切り出す手順をとります。

### Terminalizer

[Terminalizer](https://github.com/faressoft/terminalizer) は対話セッションを記録した後に、生成された YAML ファイル上でフレームの間引きや遅延時間の調整を行い、GIF 画像や Web プレイヤーを書き出せます。Node.js ランタイムとネイティブアドオンのコンパイル環境（node-gyp）が必要です。

### console2svg

`console2svg interactive` は、端末で作業しながらファンクションキー（`F10` で静止画、`F9` で動画）を押すことで、その場でタイムスタンプ付きの SVG または動画を保存します。作業を終了せずに必要な画面だけを切り出すことが可能です。

一方で、Terminalizer のように記録されたフレームを事後的に YAML で 1 フレームずつ編集したり、タイミングを微修正したりする編集機能は備えていません。撮影時の画面状態がそのままファイルに出力されます。

## 画面のリアルタイム配信と Web 共有

[`live-server`](../utilities/live-server.md) に相当する機能です。ターミナルの表示内容をリアルタイムに Web ブラウザへ中継します。

| ツール | 配信プロトコル | ブラウザ側の表示形態 | 対話操作（入力） | 外部サーバー依存 | リポジトリ指標 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg**](https://github.com/arika0093/console2svg) | HTTP / Server-Sent Events（SSE） | 3 層分離 SVG（ベクター画像） | なし（表示専用） | 不要（内蔵サーバー） | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**ttyd**](https://github.com/tsl0922/ttyd) | WebSocket | xterm.js（Canvas / WebGL） | 可能（`-W` で書き込み許可） | 不要（内蔵サーバー） | [![GitHub last commit](https://img.shields.io/github/last-commit/tsl0922/ttyd)](https://github.com/tsl0922/ttyd) [![GitHub stars](https://img.shields.io/github/stars/tsl0922/ttyd)](https://github.com/tsl0922/ttyd) |
| [**GoTTY**](https://github.com/yudai/gotty) | WebSocket | xterm.js / hterm | 可能（`-w` で書き込み許可） | 不要（内蔵サーバー） | [![GitHub last commit](https://img.shields.io/github/last-commit/yudai/gotty)](https://github.com/yudai/gotty) [![GitHub stars](https://img.shields.io/github/stars/yudai/gotty)](https://github.com/yudai/gotty) |
| [**WeTTY**](https://github.com/butlerx/wetty) | WebSocket | xterm.js | 可能（SSH 経由） | 不要（Node.js サーバー） | [![GitHub last commit](https://img.shields.io/github/last-commit/butlerx/wetty)](https://github.com/butlerx/wetty) [![GitHub stars](https://img.shields.io/github/stars/butlerx/wetty)](https://github.com/butlerx/wetty) |
| [**asciinema streaming**](https://github.com/asciinema/asciinema) | WebSocket（ALiS / asciicast） | asciinema-player | なし（視聴専用） | 必要（asciinema-server） | [![GitHub last commit](https://img.shields.io/github/last-commit/asciinema/asciinema)](https://github.com/asciinema/asciinema) [![GitHub stars](https://img.shields.io/github/stars/asciinema/asciinema)](https://github.com/asciinema/asciinema) |

### ttyd / GoTTY / WeTTY

[ttyd](https://github.com/tsl0922/ttyd) や [GoTTY](https://github.com/yudai/gotty) は、ターミナルエミュレーター（xterm.js など）をブラウザ上で動かし、Web 経由でシェルを操作可能にする Web 端末サーバーです。[WeTTY](https://github.com/butlerx/wetty) はブラウザから SSH 接続を行う Node.js 製のツールです。

これらはブラウザ側からの入力を受け付け、ターミナルを対話的に操作することを主目的とします。表示は DOM や Canvas による端末エミュレーションであり、SVG 画像の生成や配信は行いません。

### asciinema live streaming

[asciinema CLI](https://docs.asciinema.org/manual/server/streaming/) は、端末の操作イベントを WebSocket 経由でリレーサーバー（asciinema-server）へ送信し、複数の視聴者が Web プレイヤー上で同時に閲覧できるライブストリーミング機能を備えています。

配信者（CLI）と視聴者（ブラウザ）の間に asciinema-server を介在させる構成をとります。

### console2svg

`console2svg live-server` は、サーバー側でターミナル画面を SVG 画像にレンダリングし、Server-Sent Events（SSE）経由で一方向のベクターストリームとしてブラウザへ配信します。外部サーバーを必要とせず、ローカルの組み込み HTTP サーバーから直接ブラウザへ SVG データを配信します。

一方、ブラウザ側からのキーボード入力やシェル操作を受け付ける機能は持ちません。また、HTTP/SSE で SVG 全体（または差分レイヤー）を配信する仕組みであるため、ネットワーク転送量が多くなる傾向にあります。このことから、リモートで直接閲覧するというよりは、OBSなどの配信ソフトで画面をキャプチャして配信する用途に向いています。

## LLM による確認・検証・修正ループ

[`session`](../for-llm/session.md) に相当する機能です。AI コーディングエージェントやスクリプトが擬似端末を起動し、画面の視覚的・テキスト的状態を確認しながら自律的に操作を進めます。

| ツール | 制御インターフェース | 画面状態の取得形式 | 待機・同期機能 | 対象プラットフォーム | リポジトリ指標 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| [**console2svg session**](https://github.com/arika0093/console2svg) | CLI（JSON 入出力） | 構造化テキスト、セル座標、SVG 画像 | `--text` 出現・消失待機（`wait`） | Linux, macOS, Windows | [![GitHub last commit](https://img.shields.io/github/last-commit/arika0093/console2svg)](https://github.com/arika0093/console2svg) [![GitHub stars](https://img.shields.io/github/stars/arika0093/console2svg)](https://github.com/arika0093/console2svg) |
| [**microsoft/tui-test**](https://github.com/microsoft/tui-test) | CLI、Rust、Python、Node.js | 画面テキスト、HTML/Trace、SVG | `expect text`、クリック操作 | Linux, macOS, Windows | [![GitHub last commit](https://img.shields.io/github/last-commit/microsoft/tui-test)](https://github.com/microsoft/tui-test) [![GitHub stars](https://img.shields.io/github/stars/microsoft/tui-test)](https://github.com/microsoft/tui-test) |
| [**pproenca/agent-tui**](https://github.com/pproenca/agent-tui) | CLI（JSON/Text 入出力）、WebSocket | 画面テキスト、ANSI スクリーンショット | `wait`（画面安定・文字列待機） | Unix 系（Linux, macOS） | [![GitHub last commit](https://img.shields.io/github/last-commit/pproenca/agent-tui)](https://github.com/pproenca/agent-tui) [![GitHub stars](https://img.shields.io/github/stars/pproenca/agent-tui)](https://github.com/pproenca/agent-tui) |
| [**tmux + MCP サーバー**](https://github.com/nickgnd/tmux-mcp) | Model Context Protocol（JSON-RPC） | ペインのプレーンテキスト | なし（クライアント側のポーリング） | Unix 系（tmux 稼働環境） | [![GitHub last commit](https://img.shields.io/github/last-commit/nickgnd/tmux-mcp)](https://github.com/nickgnd/tmux-mcp) [![GitHub stars](https://img.shields.io/github/stars/nickgnd/tmux-mcp)](https://github.com/nickgnd/tmux-mcp) |

### microsoft/tui-test

[microsoft/tui-test](https://github.com/microsoft/tui-test) は、TUI アプリケーションおよびシェルのテストと自動操作を目的としたツールです。CLI だけでなく Rust、Python、Node.js のバインディングを提供し、インプロセスでの操作も可能です。

画面上のテキストの出現待機（`expect text`）や要素のクリック操作、HTML トレースビューア、SVG スクリーンショットの取得に対応しています。

### pproenca/agent-tui

[pproenca/agent-tui](https://github.com/pproenca/agent-tui) は、AI エージェントによる TUI アプリケーションの自動操作を目的とした Rust 製 CLI ツールです。バックグラウンドデーモンが複数の PTY セッションを管理し、テキスト入力やキー送信、画面の安定待機（Wait conditions）を行います。

Linux や macOS などの Unix 系 OS を対象としており、Windows のネイティブ環境は対象外となっています。

### tmux + MCP サーバー

[nickgnd/tmux-mcp](https://github.com/nickgnd/tmux-mcp) などの MCP サーバーは、tmux のコマンド群（`send-keys`, `capture-pane` など）をラップし、Model Context Protocol 経由で LLM クライアントからターミナルを操作できるようにする仕組みです。

既存の tmux セッションやペインに対してキーを送信し、ペインのプレーンテキストを取得します。画面描画の完了検知や待機コマンドは内蔵されていないため、クライアント側で状態を定期取得して判定します。

### console2svg

`console2svg session` は、バックグラウンドデーモンが PTY セッションを保持し、CLI コマンド（`start`, `send`, `read`, `wait`, `capture`, `stop`）を介して JSON 形式で画面情報やセルのスタイル属性を返します。マルチモーダル LLM 向けに SVG 画像を直接取得できるほか、Linux、macOS、Windows（ConPTY）のクロスプラットフォームで動作します。

一方、`microsoft/tui-test` のような Python や Node.js のインプロセスライブラリバインディングは提供しておらず、すべての操作は CLI プロセスの呼び出しを経由して行われます。また、Playwright 風のマウスクリック指定（テキスト位置のクリック解決など）や、Web ベースのトレースビューア機能は内蔵していません。
