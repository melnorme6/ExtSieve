import { canonicalUrl } from '../lib/site-settings';

export function GET() {
  return new Response(`<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><url><loc>${canonicalUrl}</loc></url></urlset>\n`,
    { headers: { 'Content-Type': 'application/xml; charset=utf-8' } });
}
