import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const docsRoot = fileURLToPath(new URL('../../../docs/', import.meta.url));

export default function rewriteDocLinks() {
  return (tree, file) => {
    const visit = (node) => {
      if (node.type === 'link' && typeof node.url === 'string') {
        const match = node.url.match(/^((?:\.\.?\/)+)([^#?]+)([?#].*)?$/);
        if (match) {
          const targetBase = path.resolve(path.dirname(file.path), match[1] + match[2]);
          const targets = /\.(?:md|mdx)$/i.test(match[2])
            ? [targetBase]
            : [`${targetBase}.md`, `${targetBase}.mdx`];
          const target = targets.find((candidate) => fs.existsSync(candidate));
          if (!target) return;

          const relativeTarget = path.relative(docsRoot, target).replace(/\\/g, '/');
          const [locale, ...slugParts] = relativeTarget.split('/');
          if (!['en', 'zh', 'ja'].includes(locale)) return;
          const slug = slugParts.join('/').replace(/\.(?:md|mdx)$/i, '');
          node.url = `/${locale}/${slug === 'index' ? '' : `${slug}/`}${match[3] ?? ''}`;
        }
      }
      for (const child of node.children ?? []) visit(child);
    };
    for (const node of tree.children ?? []) visit(node);
  };
}
