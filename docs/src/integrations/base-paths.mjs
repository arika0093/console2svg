function normalizeBase(base) {
  if (!base || base === '/') return '';
  return `/${String(base).replace(/^\/+|\/+$/g, '')}`;
}

function withBase(value, base) {
  if (
    typeof value !== 'string' ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    !base ||
    value === base ||
    value.startsWith(`${base}/`)
  ) {
    return value;
  }

  return `${base}${value}`;
}

function walk(node, visit) {
  if (!node || typeof node !== 'object') return;
  visit(node);
  if (!Array.isArray(node.children)) return;
  for (const child of node.children) walk(child, visit);
}

export default function prefixBasePaths(options = {}) {
  const base = normalizeBase(options.base);

  return function transform(tree) {
    if (!base) return;

    walk(tree, (node) => {
      if (node.properties && typeof node.properties === 'object') {
        for (const name of ['href', 'src']) {
          if (typeof node.properties[name] === 'string') {
            node.properties[name] = withBase(node.properties[name], base);
          }
        }
      }

      if (Array.isArray(node.attributes)) {
        for (const attribute of node.attributes) {
          if (
            attribute &&
            (attribute.name === 'href' || attribute.name === 'src') &&
            typeof attribute.value === 'string'
          ) {
            attribute.value = withBase(attribute.value, base);
          }
        }
      }
    });
  };
}
