// Minimal Electron ASAR extractor (header parse only, no dependencies).
// Usage: node extract-asar.mjs <app.asar> <entry> <outFile>
// e.g.  node extract-asar.mjs app.asar lib/main.js main.js
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname } from 'node:path'

const [, , asarPath, entry, outPath] = process.argv
const buf = readFileSync(asarPath)
const headerSize = buf.readUInt32LE(4)      // total pickle size (excluding the leading uint32)
const jsonSize = buf.readUInt32LE(12)       // string length inside the pickle
const header = JSON.parse(buf.subarray(16, 16 + jsonSize).toString('utf8'))
const dataOffset = 8 + headerSize

function find(node, parts) {
  let cur = node
  for (const part of parts) {
    if (!cur.files || !cur.files[part]) return null
    cur = cur.files[part]
  }
  return cur
}

const node = find(header, entry.split('/'))
if (!node || node.offset === undefined || node.files) {
  console.error('entry not found or is a directory:', entry)
  process.exit(1)
}
const start = dataOffset + Number(node.offset)
const size = Number(node.size)
mkdirSync(dirname(outPath), { recursive: true })
writeFileSync(outPath, buf.subarray(start, start + size))
console.log('extracted', entry, '->', outPath, size, 'bytes')
