import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {gzipSync} from 'node:zlib';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {syncSharedSource} from './sync-shared-source.mjs';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(scriptDir, '..');
const sourceRoot = syncSharedSource();
const sourceDataRoot = path.join(sourceRoot, 'public', 'data');
const imageCacheRoot = path.join(sourceRoot, 'assets', 'question-images');
const originalImageRoot = path.join(sourceRoot, 'public', 'question-images');
const outputRoot = path.join(projectRoot, 'output', 'EmberIntentQuiz');
const {applyQuestionReviews, answerIndices, isEligibleQuestion} = await import(
  pathToFileURL(path.join(sourceRoot, 'packages', 'question-core', 'index.js')).href
);
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

function beneath(root, relative) {
  const base = path.resolve(root);
  const file = path.resolve(base, relative);
  if (file !== base && !file.startsWith(base + path.sep)) {
    throw new Error('Refusing a path outside ' + base + ': ' + relative);
  }
  return file;
}

function packageQuestionImages(imageEntries) {
  const bySource = new Map(imageEntries.map(entry => [entry.source, entry]));
  const copied = new Map();
  const pendingFiles = new Map();

  const rewrite = (value, questionId) => String(value || '').replace(
    /!\[([^\]]*)\]\(([^)\n]+)\)/g,
    (markdown, alt, url) => {
      if (!url.startsWith('/question-images/')) return markdown;
      const source = url.slice('/question-images/'.length);
      const entry = bySource.get(source);
      let assetRoot;
      let assetPath;
      let kind;
      let expectedHash = null;
      if (entry) {
        if (!entry.pixelsVerified || !/^[0-9a-f]{64}$/.test(entry.webpSha256 || '')) {
          throw new Error('Image cache manifest does not verify ' + source);
        }
        assetRoot = imageCacheRoot;
        assetPath = entry.webp;
        expectedHash = entry.webpSha256;
        kind = 'verified-webp';
      } else {
        if (!/\.(png|webp|jpe?g)$/i.test(source)) {
          throw new Error('Question ' + questionId + ' references an unsupported original image: ' + source);
        }
        assetRoot = originalImageRoot;
        assetPath = source;
        kind = 'source-image';
      }

      const relativeOutput = path.posix.join('images', assetPath.replaceAll('\\', '/'));
      if (!copied.has(relativeOutput)) {
        const sourceFile = beneath(assetRoot, assetPath);
        const bytes = fs.readFileSync(sourceFile);
        const sha256 = hash(bytes);
        if (expectedHash && sha256 !== expectedHash) {
          throw new Error('Cached image hash mismatch: ' + source);
        }
        if (!bytes.length) throw new Error('Question image is empty: ' + source);
        pendingFiles.set(relativeOutput, bytes);
        copied.set(relativeOutput, {source, bytes: bytes.length, sha256, kind});
      }
      return '[[IMAGE:' + relativeOutput + ']]';
    },
  );

  const flush = () => {
    for (const [relative, bytes] of pendingFiles) {
      const outputFile = beneath(outputRoot, relative);
      fs.mkdirSync(path.dirname(outputFile), {recursive: true});
      fs.writeFileSync(outputFile, bytes);
    }
  };
  return {rewrite, copied, flush};
}

function main() {
  const manifest = readJson(path.join(sourceDataRoot, 'manifest.json'));
  if (!Array.isArray(manifest.questionFiles) || !Number.isSafeInteger(manifest.counts?.total)
      || !Number.isSafeInteger(manifest.counts?.reviewed)
      || !Number.isSafeInteger(manifest.counts?.usable)
      || !/^[0-9a-f]{64}$/.test(manifest.answerReviewSha256 || '')) {
    throw new Error('Question manifest is incomplete; refusing to package an unverified bank.');
  }

  const reviewsBytes = fs.readFileSync(path.join(sourceDataRoot, 'answer-reviews.json'));
  if (hash(reviewsBytes) !== manifest.answerReviewSha256) {
    throw new Error('Answer-review ledger hash does not match the question manifest.');
  }
  const reviews = JSON.parse(reviewsBytes.toString('utf8'));
  if (!Array.isArray(reviews) || reviews.length !== manifest.counts.reviewed) {
    throw new Error('Answer-review ledger count does not match the question manifest.');
  }

  const reviewById = new Map();
  for (const review of reviews) {
    const id = String(review?.id ?? '');
    if (!/^\d{4,}$/.test(id) || reviewById.has(id)) {
      throw new Error('Invalid or duplicate review question id: ' + (id || '(empty)'));
    }
    reviewById.set(id, review);
  }

  const questions = [];
  for (const file of manifest.questionFiles) {
    const relative = file.replace(/^\/data\//, '');
    const data = readJson(beneath(sourceDataRoot, relative));
    if (!Array.isArray(data)) throw new Error('Expected a question array in ' + file);
    questions.push(...data);
  }
  if (questions.length !== manifest.counts.total) {
    throw new Error('Question count mismatch: expected ' + manifest.counts.total + ', found ' + questions.length);
  }

  const questionIds = new Set();
  for (const question of questions) {
    const id = String(question?.sourceId ?? question?.id ?? '');
    if (!id || questionIds.has(id)) {
      throw new Error('Missing or duplicate source question id: ' + (id || '(empty)'));
    }
    questionIds.add(id);
  }
  for (const id of reviewById.keys()) {
    if (!questionIds.has(id)) throw new Error('Review ledger refers to a missing question: ' + id);
  }

  const effectiveQuestions = applyQuestionReviews(questions, reviews);
  const eligible = [];
  let excludedMultiAnswer = 0;
  let usableCount = 0;
  let invalidCount = 0;
  for (const question of effectiveQuestions) {
    if (question.usable === false) continue;
    usableCount += 1;
    if (!isEligibleQuestion(question)) {
      invalidCount += 1;
      continue;
    }
    if (!isEligibleQuestion(question, {singleAnswerOnly: true})) {
      excludedMultiAnswer += 1;
      continue;
    }
    const answers = answerIndices(question);
    eligible.push({
      id: String(question.id),
      category: String(question.category || question.module || '行测真题'),
      exam: String(question.exam || ''),
      year: String(question.year || ''),
      stem: question.stem || '',
      material: question.material || '',
      options: question.options,
      answer: answers[0],
    });
  }

  if (usableCount !== manifest.counts.usable
      || eligible.length + excludedMultiAnswer + invalidCount !== usableCount
      || invalidCount !== 0) {
    throw new Error('Usable-question accounting mismatch: expected ' + manifest.counts.usable
      + ', found ' + eligible.length + ' single-answer + ' + excludedMultiAnswer
      + ' multi-answer + ' + invalidCount + ' invalid.');
  }
  if (!eligible.length) throw new Error('No usable single-answer questions were found.');

  const imageEntries = readJson(path.join(imageCacheRoot, 'manifest.json'));
  if (!Array.isArray(imageEntries)) throw new Error('Optimized question-image manifest is invalid.');
  const imageBySource = new Map(imageEntries.map(entry => [entry.source, entry]));
  const requiredImagePaths = new Set();
  const imagePattern = /!\[([^\]]*)\]\(([^)\n]+)\)/g;
  for (const question of eligible) {
    const content = [question.stem, question.material, ...question.options].join('\n');
    for (const match of content.matchAll(imagePattern)) {
      const url = match[2];
      if (!url.startsWith('/question-images/')) continue;
      const source = url.slice('/question-images/'.length);
      const entry = imageBySource.get(source);
      if (entry && (!entry.pixelsVerified || !/^[0-9a-f]{64}$/.test(entry.webpSha256 || ''))) {
        throw new Error('Image cache manifest does not verify ' + source);
      }
      const relativeAsset = entry
        ? 'assets/question-images/' + entry.webp
        : 'public/question-images/' + source;
      requiredImagePaths.add(relativeAsset.replaceAll('\\', '/'));
    }
  }
  syncSharedSource([...requiredImagePaths]);
  const {rewrite, copied, flush} = packageQuestionImages(imageEntries);
  const packagedQuestions = eligible.map(question => ({
    ...question,
    stem: rewrite(question.stem, question.id),
    material: rewrite(question.material, question.id),
    options: question.options.map(option => rewrite(option, question.id)),
  }));
  fs.mkdirSync(outputRoot, {recursive: true});
  flush();

  const sourceLock = readJson(path.join(projectRoot, 'question-source.lock.json'));
  const bank = {
    sharedSourceRepository: sourceLock.repository,
    sharedSourceCommit: sourceLock.commit,
    sourceRepository: manifest.sourceRepository,
    sourceCommit: manifest.sourceCommit,
    sourceReviewCommit: manifest.sourceReviewCommit,
    questions: packagedQuestions,
  };
  const compressed = gzipSync(Buffer.from(JSON.stringify(bank)), {level: 9});
  fs.writeFileSync(path.join(outputRoot, 'question-bank.json.gz'), compressed);
  fs.copyFileSync(
    path.join(projectRoot, 'EmberIntentQuiz', 'EmberIntentQuiz.json'),
    path.join(outputRoot, 'EmberIntentQuiz.json'),
  );
  fs.writeFileSync(path.join(outputRoot, 'QUESTION-DATA-NOTICE.txt'), [
    'Shared bank and rules: ' + sourceLock.repository + ' @ ' + sourceLock.commit,
    'Question source: ' + manifest.sourceRepository,
    'Question content commit: ' + manifest.sourceCommit,
    'Answer-review commit: ' + manifest.sourceReviewCommit,
    'The source repository states that exam questions and images remain copyrighted by their original institutions and platforms.',
    'This local package is generated for personal, non-commercial use. Do not redistribute it without permission from the relevant rights holders.',
    '',
  ].join('\n'));

  const imageBytes = [...copied.values()].reduce((total, size) => total + size, 0);
  console.log('Packaged ' + packagedQuestions.length.toLocaleString()
    + ' single-answer questions; excluded ' + excludedMultiAnswer + ' multi-answer questions.');
  const optimizedImageCount = [...copied.values()].filter(image => image.kind === 'verified-webp').length;
  const sourceImageCount = copied.size - optimizedImageCount;
  console.log('Copied ' + optimizedImageCount.toLocaleString() + ' verified WebP and '
    + sourceImageCount.toLocaleString() + ' original images ('
    + (imageBytes / 1048576).toFixed(2) + ' MiB total).');
  fs.writeFileSync(path.join(outputRoot, 'question-images-manifest.json'), JSON.stringify(
    [...copied.entries()].map(([file, image]) => ({file, ...image})),
    null,
    2,
  ) + '\\n');
  console.log('Question bank: ' + path.relative(projectRoot, path.join(outputRoot, 'question-bank.json.gz'))
    + ' (' + (compressed.length / 1048576).toFixed(2) + ' MiB gzip).');
}

main();
