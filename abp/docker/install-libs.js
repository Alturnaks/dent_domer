// Аналог `abp install-libs` без ABP CLI: копирует клиентские библиотеки из node_modules в wwwroot/libs
// по картам abp.resourcemapping.js пакетов @abp/* (и проекта). Запуск: node install-libs.js <папка Web-проекта>
'use strict';
const fs = require('fs');
const path = require('path');

const webDir = path.resolve(process.argv[2] || '.');
const nm = path.join(webDir, 'node_modules');
const aliases = { '@node_modules': './node_modules', '@libs': './wwwroot/libs' };

function mappingFiles() {
  const files = [];
  if (!fs.existsSync(nm)) throw new Error('node_modules not found in ' + webDir + ' (run yarn install first)');
  for (const entry of fs.readdirSync(nm)) {
    const dirs = entry.startsWith('@')
      ? fs.readdirSync(path.join(nm, entry)).map(p => path.join(nm, entry, p))
      : [path.join(nm, entry)];
    for (const d of dirs) {
      const f = path.join(d, 'abp.resourcemapping.js');
      if (fs.existsSync(f)) files.push(f);
    }
  }
  const own = path.join(webDir, 'abp.resourcemapping.js');
  if (fs.existsSync(own)) files.push(own);
  return files;
}

function resolveAlias(p, localAliases) {
  const all = Object.assign({}, aliases, localAliases);
  for (const [k, v] of Object.entries(all)) {
    if (p === k || p.startsWith(k + '/')) p = v + p.substring(k.length);
  }
  return path.resolve(webDir, p);
}

function globToRegex(glob) {
  let re = '';
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*') {
      if (glob[i + 1] === '*') {
        i++;
        if (glob[i + 1] === '/') { i++; re += '(?:.*/)?'; } else { re += '.*'; }
      } else re += '[^/]*';
    } else if (c === '?') re += '[^/]';
    else if (c === '{') { const end = glob.indexOf('}', i); re += '(' + glob.substring(i + 1, end).split(',').map(escape).join('|') + ')'; i = end; }
    else re += escape(c);
  }
  return new RegExp('^' + re + '$');
}
function escape(s) { return s.replace(/[.+^$()|[\]\\]/g, '\\$&'); }

function walk(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out); else out.push(p);
  }
  return out;
}

let copied = 0;
function copy(src, destDir, rel) {
  const target = path.join(destDir, rel);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(src, target);
  copied++;
}

const libs = resolveAlias('@libs', {});
fs.rmSync(libs, { recursive: true, force: true });
for (const file of mappingFiles()) {
  const m = require(file);
  const local = m.aliases || {};
  for (const [from, to] of Object.entries(m.mappings || {})) {
    const src = resolveAlias(from, local).split(path.sep).join('/');
    const dest = resolveAlias(to, local);
    const firstGlob = src.search(/[*?{]/);
    if (firstGlob < 0) {
      if (!fs.existsSync(src)) { console.warn('skip (missing): ' + from); continue; }
      if (fs.statSync(src).isDirectory()) {
        for (const f of walk(src, [])) copy(f, dest, path.relative(src, f));
      } else copy(src, dest, path.basename(src));
      continue;
    }
    const base = src.substring(0, src.lastIndexOf('/', firstGlob));
    if (!fs.existsSync(base)) { console.warn('skip (missing): ' + from); continue; }
    const re = globToRegex(src.substring(base.length + 1));
    for (const f of walk(base, [])) {
      const rel = path.relative(base, f).split(path.sep).join('/');
      if (re.test(rel)) copy(f, dest, rel);
    }
  }
}
console.log('install-libs: copied ' + copied + ' files to ' + libs);
