import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { screenshotSources, screenshotCrops } from '../src/lib/screenshot-config.ts';

// Resolve the existing image engine through the declared, locked Astro dependency.
export const sharp = createRequire(import.meta.resolve('astro'))('sharp');
const sourceRoot = new URL('../src/assets/screenshots/', import.meta.url);
export const detailRoot = new URL('../public/screenshots/details/', import.meta.url);

export function cropRectangle(crop) {
  return Object.fromEntries(['left', 'top', 'width', 'height'].map(key => [key, crop[key]]));
}

async function writeChanged(name, bytes) {
  const destination = new URL(name, detailRoot);
  let previous;
  try { previous = await readFile(destination); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (!previous?.equals(bytes)) await writeFile(destination, bytes);
}

export function validateRectangle(rectangle, source) {
  for (const key of ['left', 'top', 'width', 'height']) {
    if (!Number.isSafeInteger(rectangle[key]) || rectangle[key] < (key === 'left' || key === 'top' ? 0 : 1)) {
      throw new Error(`Invalid crop ${key}`);
    }
  }
  if (rectangle.left + rectangle.width > source.width || rectangle.top + rectangle.height > source.height) {
    throw new Error('Crop exceeds source dimensions');
  }
}

export async function prepareScreenshots() {
  const sources = new Map();
  for (const [key, source] of Object.entries(screenshotSources)) {
    const bytes = await readFile(new URL(source.file, sourceRoot));
    const metadata = await sharp(bytes).metadata();
    if (createHash('sha256').update(bytes).digest('hex') !== source.sha256 ||
      metadata.width !== source.width || metadata.height !== source.height) {
      throw new Error(`Screenshot changed: review ${source.file} and its crop configuration`);
    }
    sources.set(key, bytes);
  }
  await mkdir(detailRoot, { recursive: true });
  for (const { id, source, ...crop } of screenshotCrops) {
    const rectangle = cropRectangle(crop);
    validateRectangle(rectangle, screenshotSources[source]);
    const result = await sharp(sources.get(source)).extract(rectangle).png({ compressionLevel: 9 }).toBuffer();
    await writeChanged(`${id}.png`, result);
    await writeChanged(`${id}.webp`, await sharp(result).webp({ lossless: true, effort: 6 }).toBuffer());
  }
  for (const key of ['light', 'dark']) {
    await writeChanged(`${key}-full.webp`, await sharp(sources.get(key)).webp({ lossless: true, effort: 6 }).toBuffer());
  }
}
