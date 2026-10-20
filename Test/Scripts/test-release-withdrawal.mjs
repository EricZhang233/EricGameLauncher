import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const workflow = readFileSync(new URL('../../.github/workflows/post_release.yml', import.meta.url), 'utf8');
const source = workflow.split(/\r?\n  draft_failed_release:/)[1].split(/script: \|\r?\n/)[1].split(/\r?\n/).map(line => line.slice(12)).join('\n');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const execute = new AsyncFunction('github', 'context', 'core', source);
let count = 0;
const apiError = status => Object.assign(new Error('fixture API error'), {status});

async function check(name, options, expectedUpdate, expectedError = false) {
  const release = {id: 17, tag_name: 'v1.2.5', published_at: '2026-10-20T12:00:00Z'};
  const original = {...release, draft: false, body: 'release description', name: 'release title', prerelease: true, assets: [{id: 29}]};
  let current = {...original, ...options.current};
  const updates = [];
  let error;
  const github = {rest: {repos: {
    getRelease: async args => {
      assert.deepEqual(args, {owner: 'fixture', repo: 'fixture', release_id: 17});
      if (options.lookupError) throw apiError(options.lookupError);
      return {data: current};
    },
    updateRelease: async args => {
      assert.deepEqual(args, {owner: 'fixture', repo: 'fixture', release_id: 17, draft: true});
      if (options.updateError) throw apiError(options.updateError);
      updates.push(args);
      current = {...current, draft: !options.rejectedDraft};
      return {data: current};
    },
  }}};
  try {
    await execute(github, {repo: {owner: 'fixture', repo: 'fixture'}, payload: {release: options.missing ? undefined : release}}, {info() {}});
  } catch (caught) { error = caught; }
  assert.equal(updates.length, expectedUpdate ? 1 : 0, name);
  assert.equal(Boolean(error), expectedError, name);
  assert.equal(current.body, original.body);
  assert.equal(current.name, original.name);
  assert.equal(current.prerelease, original.prerelease);
  assert.deepEqual(current.assets, original.assets);
  count++;
  console.log('PASS ' + name);
}

await check('returns only the triggering release to draft and preserves its content', {}, true);
await check('already drafted release is idempotent', {current: {draft: true}}, false);
await check('deleted release is not recreated', {lookupError: 404}, false);
await check('lookup permission failure remains an error', {lookupError: 403}, false, true);
await check('lookup service failure remains an error', {lookupError: 503}, false, true);
await check('changed tag identity refuses modification', {current: {tag_name: 'v9.9.9'}}, false, true);
await check('changed release ID refuses modification', {current: {id: 18}}, false, true);
await check('missing release identity refuses modification', {missing: true}, false, true);
await check('republished release is protected from an older failed run', {current: {published_at: '2026-10-20T13:00:00Z'}}, false);
await check('failed draft update is not reported as success', {updateError: 403}, false, true);
await check('response without draft state remains an error', {rejectedDraft: true}, true, true);
console.log(`${count} checks passed.`);
