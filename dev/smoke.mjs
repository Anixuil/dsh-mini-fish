#!/usr/bin/env node
// 仓库自检：插件清单 / locale / 图标 / bundle patch / 宠物包完整性。
//   node dev/smoke.mjs
// 只依赖 Node 内置模块；改完插件或宠物包后跑一遍，退出码非 0 就是有问题。
import { readFileSync, existsSync, statSync, readdirSync } from 'node:fs'
import { join, dirname, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const failed = []
const notes = []

const ok = (what, detail = '') => console.log(`ok    ${what}${detail ? '  ' + detail : ''}`)
const bad = (what, detail) => {
  failed.push(`${what}: ${detail}`)
  console.log(`FAIL  ${what}  ${detail}`)
}
const note = (what, detail) => notes.push(`${what}: ${detail}`)

const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'))
const exists = (rel) => existsSync(join(ROOT, rel))
const size = (rel) => statSync(join(ROOT, rel)).size

// ---------------------------------------------------------------- 1. package.json

let pkg
try {
  pkg = readJson(join(ROOT, 'package.json'))
  ok('package.json', `${pkg.name}@${pkg.version}`)
} catch (error) {
  bad('package.json', String(error.message))
  finish()
}

if (!pkg.name || !pkg.version) bad('package.json', 'name / version 缺失')
if (pkg.type !== 'module') bad('package.json', 'type 必须是 module')
if (!pkg.dsh || !pkg.dsh.bundle || !pkg.dsh.bundle.patch) bad('package.json', '缺少 dsh.bundle.patch')
if (!pkg.exports || !pkg.exports['.']) bad('package.json', '缺少 exports["."]')
if (pkg.license !== 'MIT') note('package.json', `license 是 ${pkg.license}`)

const patchPath = pkg.dsh?.bundle?.patch?.replace(/^\.\//, '')
if (patchPath && !exists(patchPath)) bad('bundle patch', `${patchPath} 不存在`)
if (patchPath && exists(patchPath)) {
  const patch = readFileSync(join(ROOT, patchPath), 'utf8')
  ok('bundle patch', patchPath)
  if (!patch.includes(`name: '${pkg.name}'`) && !patch.includes(`name: "${pkg.name}"`)) {
    bad('bundle patch', `没有引用包名 ${pkg.name}（行名必须能解析到这个包）`)
  }
  const ids = [...patch.matchAll(/^\s*-\s*id:\s*(\S+)/gm)].map((m) => m[1])
  const dup = ids.filter((id, i) => ids.indexOf(id) !== i)
  if (dup.length) bad('bundle patch', `重复的行 id: ${[...new Set(dup)].join(', ')}`)
  else if (ids.length) ok('bundle patch rows', ids.join(', '))
}

// 运行需要的文件必须被 files 覆盖（本地 link 安装不吃 files，发布/打包吃）
const runtimeNeed = ['index.js', patchPath, 'locale/en.json', 'locale/zh.json', 'icon.svg', 'companion/src/PetHost.cs']
const filesList = Array.isArray(pkg.files) ? pkg.files : []
const covered = (need) => filesList.some((entry) => {
  const e = entry.replace(/\/\*\*$/, '').replace(/\/$/, '')
  if (entry.includes('*')) return need.startsWith(e.split('*')[0])
  return need === e || need.startsWith(e + '/')
})
for (const need of runtimeNeed.filter(Boolean)) {
  if (!exists(need)) bad('files', `${need} 不存在`)
  else if (!covered(need)) bad('files', `${need} 没有被 package.json files 覆盖`)
}
ok('files 覆盖', `${runtimeNeed.filter(Boolean).length} 项运行时文件`)

// ---------------------------------------------------------------- 2. 插件导出

try {
  const mod = await import(pathToFileURL(join(ROOT, 'index.js')).href)
  if (typeof mod.apply !== 'function') bad('index.js', '没有导出 apply(ctx, config)')
  else ok('index.js', '导出 apply()')
} catch (error) {
  bad('index.js', String(error.message))
}

// ---------------------------------------------------------------- 3. locale / icon

for (const lang of ['en', 'zh']) {
  const rel = `locale/${lang}.json`
  if (!exists(rel)) {
    bad('locale', `${rel} 不存在`)
    continue
  }
  try {
    const meta = readJson(join(ROOT, rel)).meta || {}
    if (!meta.title || !meta.description) bad(rel, 'meta.title / meta.description 缺失')
    else ok(rel, `「${meta.title}」`)
  } catch (error) {
    bad(rel, String(error.message))
  }
}

if (!exists('icon.svg')) {
  bad('icon', 'icon.svg 不存在')
} else {
  const bytes = statSync(join(ROOT, 'icon.svg')).size
  const head = readFileSync(join(ROOT, 'icon.svg'), 'utf8').slice(0, 200)
  if (bytes > 256 * 1024) bad('icon', `${bytes} 字节，超过 256 KiB`)
  else if (!head.includes('<svg')) bad('icon', '不是 SVG')
  else ok('icon', `${bytes} 字节`)
}

// ---------------------------------------------------------------- 4. 宠物包

const PNG_SIG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
function pngSize(path) {
  const b = readFileSync(path)
  if (b.length < 33 || !b.subarray(0, 8).equals(PNG_SIG)) return null
  if (b.toString('ascii', 12, 16) !== 'IHDR') return null
  return { width: b.readUInt32BE(16), height: b.readUInt32BE(20) }
}

const petsDir = join(ROOT, 'pets')
const packs = existsSync(petsDir)
  ? readdirSync(petsDir, { withFileTypes: true }).filter((d) => d.isDirectory() && existsSync(join(petsDir, d.name, 'pet.json'))).map((d) => d.name)
  : []
if (!packs.length) bad('pets', 'pets/ 下没有找到宠物包')

const LIMIT_BYTES = 8 * 1024 * 1024
function walk(dir, out = []) {
  for (const d of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, d.name)
    if (d.isDirectory()) walk(p, out)
    else out.push(p)
  }
  return out
}

for (const packId of packs) {
  const dir = join(petsDir, packId)
  const manifest = readJson(join(dir, 'pet.json'))
  const label = `pack ${packId}`
  // 以 _ 开头的是给作者复制的模板（只有 manifest + 导引 + 清单，没有 parts/）
  const isTemplate = packId.startsWith('_')
  if (isTemplate) {
    if (manifest.spec !== 'dsh-pet/1') bad(label, `spec 是 ${manifest.spec}，要求 dsh-pet/1`)
    else ok(`${label}（模板，跳过图层检查）`, `${(manifest.layers || []).length} 层定义`)
    continue
  }
  if (manifest.spec !== 'dsh-pet/1') bad(label, `spec 是 ${manifest.spec}，要求 dsh-pet/1`)
  if (manifest.id !== packId) bad(label, `id(${manifest.id}) 与目录名不一致`)
  const cw = manifest.canvas?.width
  const ch = manifest.canvas?.height
  if (!(cw > 0 && ch > 0)) {
    bad(label, 'canvas.width/height 无效')
    continue
  }
  const layers = Array.isArray(manifest.layers) ? manifest.layers : []
  if (!layers.length) bad(label, '没有 layers')
  const ids = layers.map((l) => l.id)
  const dupId = ids.filter((id, i) => ids.indexOf(id) !== i)
  if (dupId.length) bad(label, `重复的层 id: ${[...new Set(dupId)].join(', ')}`)

  const imageRefs = []
  const referenced = (rel, where) => {
    imageRefs.push({ rel, where })
  }
  for (const layer of layers) {
    if (!layer.id) bad(label, '有层缺少 id')
    if (!layer.image) {
      bad(label, `层 ${layer.id} 缺少 image`)
      continue
    }
    referenced(layer.image, `layer ${layer.id}`)
    if (layer.parent && !ids.includes(layer.parent)) bad(label, `层 ${layer.id} 的 parent(${layer.parent}) 不存在`)
    for (const [key, rel] of Object.entries(layer.variants || {})) referenced(rel, `layer ${layer.id} variant ${key}`)
  }
  for (const [poseId, pose] of Object.entries(manifest.poses || {})) {
    if (!pose.image) bad(label, `pose ${poseId} 缺少 image`)
    else referenced(pose.image, `pose ${poseId}`)
    if (typeof pose.ledgeY !== 'number') bad(label, `pose ${poseId} 缺少 ledgeY`)
  }
  const rig = manifest.eyesRig
  if (rig) {
    const sides = ['l', 'r']
    for (const [state, spec] of Object.entries(rig.states || {})) {
      if (!spec.static) bad(label, `eyesRig state ${state} 缺少 static`)
      else for (const side of sides) referenced(spec.static.replace('%s', side), `eyesRig ${state} ${side}`)
      if (spec.mask) for (const side of sides) referenced(spec.mask.replace('%s', side), `eyesRig mask ${state} ${side}`)
    }
    if (!rig.moving?.image) bad(label, 'eyesRig 缺少 moving.image')
    else {
      for (const side of sides) {
        for (const suffix of Object.values(rig.pupils || { normal: '' })) {
          referenced(rig.moving.image.replace('%s', side).replace('%s', suffix), `eyesRig moving ${side}${suffix}`)
        }
      }
    }
  }

  let missing = 0
  let wrongCanvas = 0
  for (const { rel, where } of imageRefs) {
    const full = join(dir, rel)
    if (!existsSync(full)) {
      missing += 1
      bad(label, `${where}: 缺文件 ${rel}`)
      continue
    }
    const dim = pngSize(full)
    if (!dim) {
      bad(label, `${where}: ${rel} 不是 PNG`)
      continue
    }
    if (dim.width !== cw || dim.height !== ch) {
      wrongCanvas += 1
      bad(label, `${where}: ${rel} 是 ${dim.width}×${dim.height}，画布是 ${cw}×${ch}`)
    }
  }
  if (!missing && !wrongCanvas) ok(label, `${layers.length} 层 · ${imageRefs.length} 张图 · 画布 ${cw}×${ch}`)

  const total = walk(dir).reduce((sum, file) => sum + statSync(file).size, 0)
  if (total > LIMIT_BYTES) bad(label, `包体 ${(total / 1048576).toFixed(2)} MB，超过 DPF v1 的 8 MB 上限`)
  else ok(`${label} 体积`, `${(total / 1048576).toFixed(2)} MB / 8 MB`)
}

// ---------------------------------------------------------------- 5. 结果

function finish() {
  if (failed.length) {
    console.log(`\n${failed.length} 项失败：`)
    for (const line of failed) console.log('  - ' + line)
    process.exit(1)
  }
  for (const line of notes) console.log(`note  ${line}`)
  console.log('\n自检通过。')
  process.exit(0)
}
finish()
