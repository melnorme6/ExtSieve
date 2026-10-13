import { completeRelease, fakeService, api } from './releases.mjs';

const scenario = process.env.SITE_TEST_SCENARIO;
const fixture = completeRelease();
let overrides = {};
if (scenario === 'fallback') overrides = { [`${api}/releases/latest`]: () => new Response('', { status: 503 }) };
else if (scenario === 'no-stable') overrides = {
  [`${api}/releases/latest`]: () => new Response('', { status: 404 }),
  [api]: { full_name: 'melnorme6/ExtSieve', private: false }, [`${api}/releases?per_page=100`]: [],
};
else if (scenario === 'incomplete') fixture.payload.assets.pop();
else if (scenario === 'malformed') overrides = { [`${api}/releases/latest`]: () => new Response('{invalid', { headers: { 'content-type': 'application/json' } }) };
else if (scenario !== 'complete') throw new Error('Unknown synthetic build scenario');
globalThis.fetch = fakeService(fixture, overrides).dependencies.fetch;
