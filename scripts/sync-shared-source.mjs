import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const lockPath = path.join(projectRoot, 'question-source.lock.json');
const checkoutPath = path.join(projectRoot, '.cache', 'question-source');

function git(args, cwd = checkoutPath) {
  return execFileSync('git', args, {
    cwd, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'inherit'],
  }).trim();
}

function validateImagePaths(imagePaths) {
  return [...new Set(imagePaths)].map(relative => {
    if (typeof relative !== 'string' || !relative || relative.startsWith('/')
      || relative.split(/[\\/]/).includes('..') || /[\r\n*?[\]]/.test(relative)) {
      throw new Error('Invalid shared-source path: ' + relative);
    }
    const normalized = relative.replaceAll('\\', '/');
    if (!normalized.startsWith('assets/question-images/')
      && !normalized.startsWith('public/question-images/')) {
      throw new Error('Shared-source files must be question images: ' + normalized);
    }
    return normalized;
  });
}

export function syncSharedSource(imagePaths = []) {
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  if (!/^https:\/\/github\.com\/Mentat-Uran\/ember-archive(?:\.git)?$/.test(lock.repository || '')
    || !/^[0-9a-f]{40}$/.test(lock.commit || '')) {
    throw new Error('question-source.lock.json must contain the canonical source repository and a full commit SHA.');
  }

  fs.mkdirSync(path.dirname(checkoutPath), {recursive: true});
  if (!fs.existsSync(path.join(checkoutPath, '.git'))) {
    if (fs.existsSync(checkoutPath) && fs.readdirSync(checkoutPath).length > 0) {
      throw new Error('Refusing to replace non-Git shared-source cache: ' + checkoutPath);
    }
    execFileSync('git', [
      'clone', '--filter=blob:none', '--no-checkout', '--no-tags',
      lock.repository, checkoutPath,
    ], {stdio: 'inherit'});
  }

  const actualRemote = git(['remote', 'get-url', 'origin']);
  if (actualRemote.replace(/\.git$/, '') !== lock.repository.replace(/\.git$/, '')) {
    throw new Error('Shared-source cache origin does not match the lock file: ' + actualRemote);
  }
  const hasSparseWorktree = fs.existsSync(path.join(checkoutPath, 'packages', 'question-core', 'index.js'));
  if (hasSparseWorktree && git(['status', '--porcelain', '--untracked-files=all'])) {
    throw new Error('Shared-source cache has local changes. Preserve or remove that cache manually, then retry.');
  }

  let sparseCheckoutEnabled = false;
  try {
    sparseCheckoutEnabled = git(['config', '--bool', 'core.sparseCheckout']) === 'true';
  } catch (error) {
    if (error.status !== 1) throw error;
  }
  if (!sparseCheckoutEnabled) git(['sparse-checkout', 'init', '--no-cone']);
  const patterns = [
    '/packages/question-core/',
    '/public/data/',
    '/assets/question-images/manifest.json',
    ...validateImagePaths(imagePaths).map(relative => '/' + relative),
  ];
  git(['sparse-checkout', 'set', '--no-cone', '--', ...patterns]);
  git(['fetch', '--depth=1', 'origin', lock.commit]);
  git(['checkout', '--detach', 'FETCH_HEAD']);

  const actualCommit = git(['rev-parse', 'HEAD']);
  if (actualCommit !== lock.commit) {
    throw new Error('Fetched shared source ' + actualCommit + ', expected ' + lock.commit + '.');
  }
  if (git(['status', '--porcelain', '--untracked-files=all'])) {
    throw new Error('Shared-source cache is not clean after checkout; preserve it and inspect the cache.');
  }
  for (const relative of [
    'packages/question-core/index.js',
    'public/data/manifest.json',
    'assets/question-images/manifest.json',
    ...validateImagePaths(imagePaths),
  ]) {
    if (!fs.existsSync(path.join(checkoutPath, relative))) {
      throw new Error('Pinned source is missing required path: ' + relative);
    }
  }
  console.log('Using shared question source ' + actualCommit + '.');
  return checkoutPath;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  syncSharedSource();
}
