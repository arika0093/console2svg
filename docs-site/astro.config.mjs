import { defineConfig } from 'astro/config';
import mdx from '@astrojs/mdx';
import starlight from '@astrojs/starlight';
import starlightGithubAlerts from 'starlight-github-alerts';
import starlightThemeNova from 'starlight-theme-nova';
import starlightVersions from 'starlight-versions';
import rewriteDocLinks from './src/remark/rewrite-doc-links.mjs';
import path from 'node:path';

// CI/release builds set CONSOLE2SVG_DOCS_VERSION to the published version.
// Keep the development site clearly identifiable without changing source files.
const currentDocsVersion = process.env.CONSOLE2SVG_DOCS_VERSION ?? 'develop';
const docsSite = process.env.CONSOLE2SVG_DOCS_SITE || undefined;
const docsBase = process.env.CONSOLE2SVG_DOCS_BASE || undefined;
const repoRoot = path.resolve(process.cwd(), '..');

export default defineConfig({
  site: docsSite,
  base: docsBase,
  server: {
    host: true,
    fs: {
      allow: [repoRoot],
    },
  },
  editLink: {
    baseUrl: 'https://github.com/arika0093/console2svg/edit/main/',
  },
  vite: {
    resolve: {
      dedupe: ['@astrojs/starlight'],
    },
  },
  integrations: [
    starlight({
      title: 'console2svg',
      description: 'Convert terminal output into crisp, scalable SVG images.',
      expressiveCode: true,
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/arika0093/console2svg' },
      ],
      defaultLocale: 'en',
      locales: {
        en: {
          label: 'English',
          lang: 'en',
        },
        zh: {
          label: '简体中文',
          lang: 'zh-CN',
        },
        ja: {
          label: '日本語',
          lang: 'ja',
        },
      },
      components: {
        Hero: './src/components/HomeHero.astro',
        PageTitle: './src/components/PageTitle.astro',
        ThemeProvider: 'starlight-theme-nova/components/ThemeProvider.astro',
        ThemeSelect: 'starlight-theme-nova/components/ThemeSelect.astro',
      },
      markdown: {
        processedDirs: ['../docs'],
      },
      customCss: [
        '@fontsource/jetbrains-mono/400.css',
        '@fontsource/jetbrains-mono/600.css',
        './src/styles/custom.css',
      ],
      plugins: [
        // starlightVersions({
        //   current: { label: currentDocsVersion },
        //   // Versioned pages are created by the release process. The local site
        //   // only exposes the current documentation and must not copy it at build time.
        //   exclude: ['**'],
        //   versions: [{ slug: currentDocsVersion, label: currentDocsVersion }],
        // }),
        starlightThemeNova(),
        starlightGithubAlerts(),
      ],
      sidebar: [
        {
          label: 'Getting started',
          translations: {
            'zh-CN': '入门',
            ja: 'はじめに',
          },
          items: [
            'getting-started/gallery',
            'getting-started/why-svg',
            'getting-started/installation',
            'getting-started/quick-start',
          ],
        },
        {
          label: 'Basic usage',
          translations: {
            'zh-CN': '基本用法',
            ja: '基本的な使い方',
          },
          items: [
            {
              label: 'Capturing images',
              translations: {
                'zh-CN': '捕获图像',
                ja: '静止画を撮影する',
              },
              items: [
                'basic-usage/capturing-images/overview',
                'basic-usage/capturing-images/crop',
                'basic-usage/capturing-images/convert-to-png',
              ],
            },
            {
              label: 'Capturing videos',
              translations: {
                'zh-CN': '捕获视频',
                ja: '動画を撮影する',
              },
              items: [
                'basic-usage/capturing-videos/overview',
                'basic-usage/capturing-videos/save-frames',
                'basic-usage/capturing-videos/convert-to-video',
              ],
            },
            {
              label: 'Masking secrets',
              translations: {
                'zh-CN': '遮盖敏感信息',
                ja: '機密情報マスキング',
              },
              items: [
                'basic-usage/masking-secrets/auto-masking',
                'basic-usage/masking-secrets/manual-masking',
              ],
            },
            'basic-usage/interactive-capture',
          ],
        },
        {
          label: 'Appearance',
          translations: {
            'zh-CN': '外观',
            ja: '見た目のカスタマイズ',
          },
          items: [
            'appearance/terminal-width-and-height',
            'appearance/window-and-background',
            {
              label: 'Themes',
              translations: {
                'zh-CN': '主题',
                ja: 'テーマ機能',
              },
              items: [
                'appearance/themes/built-in-themes',
                {
                  label: 'Built-in theme catalog',
                  translations: {
                    'zh-CN': '内置主题目录',
                    ja: '組み込みテーマ一覧',
                  },
                  items: [
                    {
                      label: 'Overview',
                      translations: {
                        'zh-CN': '概览',
                        ja: '概要',
                      },
                      slug: 'appearance/themes/built-in-theme-list',
                    },
                    {
                      label: 'Palettes',
                      translations: {
                        'zh-CN': '配色方案',
                        ja: 'パレット',
                      },
                      slug: 'appearance/themes/built-in-palettes',
                    },
                    {
                      label: 'Window chrome',
                      translations: {
                        'zh-CN': '窗口边框',
                        ja: 'ウインドウ',
                      },
                      slug: 'appearance/themes/built-in-window-themes',
                    },
                    {
                      label: 'Complete themes',
                      translations: {
                        'zh-CN': '完整主题',
                        ja: '一括指定',
                      },
                      slug: 'appearance/themes/built-in-full-themes',
                    },
                  ],
                },
                'appearance/themes/custom-themes',
                'appearance/themes/create-custom-theme',
              ],
            },
            {
              label: 'Advanced',
              translations: {
                'zh-CN': '高级功能',
                ja: '発展的な機能',
              },
              items: [
                'appearance/advanced/font',
                'appearance/advanced/fine-tuning',
              ],
            },
          ],
        },
        {
          label: 'Automation',
          translations: {
            'zh-CN': '自动化',
            ja: '自動化',
          },
          items: [
            'automation/replay',
            'automation/asciicast',
            'automation/document-image-sync',
            'automation/github-actions',
          ],
        },
        {
          label: 'For LLM',
          translations: {
            'zh-CN': 'LLM专用',
            ja: 'LLM向け機能',
          },
          items: [
            'for-llm/use-skill',
            'for-llm/session',
          ],
        },
        {
          label: 'Utilities',
          translations: {
            'zh-CN': '实用工具',
            ja: 'ユーティリティ機能',
          },
          items: [
            'utilities/live-server',
            'utilities/tmux',
            'utilities/shell-completion',
            'utilities/status',
            'utilities/logging',
          ],
        },
        {
          label: 'Reference',
          translations: {
            'zh-CN': '参考',
            ja: 'リファレンス',
          },
          items: [
            'reference/cli',
            'reference/cli/capture',
            'reference/cli/interactive',
            'reference/cli/replay',
            'reference/cli/cast',
            'reference/cli/theme',
            'reference/cli/status',
            'reference/cli/update',
            'reference/cli/llm',
            'reference/cli/session',
            'reference/cli/live-server',
            'reference/cli/tmux',
            'reference/cli/batch',
            'reference/cli/completions',
          ],
        },
        {
          label: 'Deep dive into console2svg',
          translations: {
            'zh-CN': '深入了解 console2svg',
            ja: 'console2svgの内部',
          },
          items: [
            'deep-dive/overview',
            'deep-dive/license',
            {
              label: 'Conversion process',
              translations: {
                'zh-CN': '转换过程',
                ja: '変換プロセス',
              },
              items: [
                'deep-dive/conversion-process/summary',
                'deep-dive/conversion-process/pty',
                'deep-dive/conversion-process/replay',
                'deep-dive/conversion-process/sequence-parsing',
                'deep-dive/conversion-process/special-fragments',
                'deep-dive/conversion-process/layer-structure',
                'deep-dive/conversion-process/animation',
                'deep-dive/conversion-process/optimization',
                'deep-dive/conversion-process/quickleaks',
                'deep-dive/conversion-process/resvg',
                'deep-dive/conversion-process/ffmpeg',
                'deep-dive/conversion-process/live-server',
              ],
            },
          ],
        },
      ],
    }),
    mdx(),
  ],
  markdown: {
    remarkPlugins: [rewriteDocLinks],
  },
});
