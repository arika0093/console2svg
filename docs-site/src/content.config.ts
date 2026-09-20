import { defineCollection } from 'astro:content';
import { glob } from 'astro/loaders';
import { i18nLoader } from '@astrojs/starlight/loaders';
import { docsSchema, i18nSchema } from '@astrojs/starlight/schema';
import { z } from 'astro/zod';

const docsLoader = () =>
  glob({
    base: new URL('../../docs/', import.meta.url),
    pattern: '{en,zh,ja}/**/[^_]*.{md,mdx}',
  });

export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: docsSchema({
      extend: z.object({
        since: z.string().optional(),
      }),
    }),
  }),
  i18n: defineCollection({ loader: i18nLoader(), schema: i18nSchema() }),
};