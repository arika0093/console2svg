---
title: Manual masking
description: Manually specify particular strings or regular expression patterns to mask with the --mask option.
since: v0.9
---

Use the `--mask` option when you have project-specific tokens, internal domain names, personal names, or other information that you want to hide individually.

## Specifying strings

Pass the string you want to mask to `--mask`.

```bash title="Terminal" "--mask"
console2svg capture --mask "1234567890abcdef" -w 100 -h 12 \
            -- echo "this value will be redacted:: 1234567890abcdef"
```

<!-- c2s::  -w 100 -h 4 --mask 1234567890abcdef -- echo "this value will be redacted:: 1234567890abcdef" -->
![echo "this value will be redacted:: 1234567890abcdef"](../../../../docs-site/public/assets/generated/ac741e6cec0f0c9d5045ad4c7819653c1134c59619e345a33655178e47d24739.svg)

## Specifying multiple patterns

You can specify the `--mask` option multiple times.

```bash title="Terminal" "--mask"
console2svg capture \
  --mask "internal-host.local" \
  --mask "admin_password" \
  --mask "user[0-9]+" \
  -- ./check.sh
```

Because regular expression patterns are also supported, you can mask systematic IDs and account names all at once.
