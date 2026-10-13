export function publicAsset(path: string): string {
  return `${import.meta.env.BASE_URL}${path}`;
}

export function imageAsset(path: string): string {
  return path.startsWith(import.meta.env.BASE_URL) ? path : publicAsset(path.replace(/^\//, ''));
}
