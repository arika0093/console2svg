import { defineMdastPlugin } from 'satteri';

const alertTypes = {
  CAUTION: 'danger',
  IMPORTANT: 'caution',
  NOTE: 'note',
  TIP: 'tip',
  WARNING: 'caution',
};

const githubAlertsPlugin = defineMdastPlugin({
  name: 'console2svg-github-alerts',
  blockquote(node, context) {
    const firstParagraph = node.children[0];
    const firstText = firstParagraph?.type === 'paragraph' ? firstParagraph.children[0] : undefined;
    if (firstText?.type !== 'text') return;

    const match = /^\[!(CAUTION|IMPORTANT|NOTE|TIP|WARNING)\]\s*/.exec(firstText.value);
    if (!match) return;

    const content = context.textContent(node).slice(match[0].length).trim();
    return { raw: `:::${alertTypes[match[1]]}\n${content}\n:::` };
  },
});

export default function githubAlerts() {
  return {
    name: 'console2svg-github-alerts',
    hooks: {
      'astro:config:setup': ({ config }) => {
        const processor = config.markdown.processor;
        if (processor?.name === 'satteri') processor.options.mdastPlugins.unshift(githubAlertsPlugin);
      },
    },
  };
}
