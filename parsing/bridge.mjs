import { parse } from 'kordoc';
import { stat } from 'node:fs/promises';

// No OCR models, remote images, cloud parsing, or document scripts are loaded.
process.env.KORDOC_OFFLINE = '1';
const source = process.argv[2];
try {
  if (!source || (await stat(source)).size > 80 * 1024 * 1024) throw new Error('Input is missing or exceeds 80 MB.');
  const result = await parse(source, { images: false, scriptTags: false, ocr: false });
  const clean = JSON.parse(JSON.stringify(result, (key, value) =>
    ['imageData', 'data', 'images', 'buffer'].includes(key) ? undefined : value));
  if (clean.markdown?.length > 400000) {
    clean.markdown = clean.markdown.slice(0, 400000);
    clean.warnings = [...(clean.warnings || []), 'Text limited to the first 400,000 characters.'];
  }
  // Limit native block data as well as the reading copy.
  let characters = 0;
  clean.blocks = (clean.blocks || []).filter(block => { characters += JSON.stringify(block).length; return characters < 1600000; });
  process.stdout.write(JSON.stringify(clean));
} catch (error) {
  process.stdout.write(JSON.stringify({ success: false, error: String(error.message || error) }));
  process.exitCode = 1;
}
