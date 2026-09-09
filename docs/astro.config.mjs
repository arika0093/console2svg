import { defineConfig } from 'astro/config';
import mdx from '@astrojs/mdx';
import starlight from '@astrojs/starlight';
import starlightGithubAlerts from 'starlight-github-alerts';
import starlightThemeNova from 'starlight-theme-nova';
import starlightVersions from 'starlight-versions';
import githubAlerts from './src/integrations/github-alerts.mjs';

// CI/release builds set CONSOLE2SVG_DOCS_VERSION to the published version.
// Keep the development site clearly identifiable without changing source files.
const currentDocsVersion = process.env.CONSOLE2SVG_DOCS_VERSION ?? 'develop';

export default defineConfig({
  integrations: [
    starlight({
      title: 'console2svg',
      description: 'Convert terminal output into crisp, scalable SVG images.',
      customCss: [
        '@fontsource/jetbrains-mono/400.css',
        '@fontsource/jetbrains-mono/600.css',
        './src/styles/custom.css',
      ],
      plugins: [
        starlightVersions({
          current: { label: currentDocsVersion },
          // Versioned pages are created by the release process. The local site
          // only exposes the current documentation and must not copy it at build time.
          exclude: ['**'],
          versions: [{ slug: currentDocsVersion, label: currentDocsVersion }],
        }),
        starlightThemeNova(),
        starlightGithubAlerts(),
      ],
      sidebar: [
        { label: 'Overview', items: ['features', 'introduction'] },
        {
          label: 'Getting started',
          items: ['getting-started/quick-start', 'getting-started/installation'],
        },
        {
          label: 'Basic usage',
          items: [
            'basic-usage/capturing-command-output',
            'basic-usage/interactive-capture',
            'basic-usage/cropping-output',
            'basic-usage/masking-sensitive-output',
            'basic-usage/converting-output-formats',
          ],
        },
        {
          label: 'Advanced usage',
          items: [
            'advanced-usage/live-server',
            'advanced-usage/tmux',
            'advanced-usage/recording-replay-and-cast-files',
            'advanced-usage/shell-completion',
          ],
        },
        {
          label: 'Appearance',
          items: [
            'appearance/themes',
            'appearance/window-chrome-and-backgrounds',
            'appearance/layout-and-typography',
          ],
        },
        {
          label: 'Automation',
          items: ['automation/ci-cd-support', 'automation/github-actions'],
        },
        {
          label: 'Reference',
          items: [
            'reference/cli-reference',
            'reference/file-formats-and-embedded-metadata',
            'reference/status-and-verbose-logs',
          ],
        },
        {
          label: 'Concepts',
          items: ['concepts/how-console2svg-works', 'concepts/svg-format-and-style'],
        },
      ],
    }),
    mdx(),
    githubAlerts(),
  ],
});
