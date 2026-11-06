import { spawn, spawnSync } from 'node:child_process'
import { appendFileSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
const log = (...args) => console.log('[dsh-pet]', ...args)

const EXE = join(HERE, 'companion', 'bin', 'PetHost.exe')
const SRC = join(HERE, 'companion', 'src', 'PetHost.cs')
const DEFAULT_PET = 'whale-maid-l1'
const FX = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319'

/** Build the companion with the in-box .NET Framework compiler when it is missing or stale. */
function ensureCompanion() {
  const csc = join(FX, 'csc.exe')
  const built = existsSync(EXE)

  // Rebuild when the source is newer than the binary. Without this an existing install
  // would keep running the old host forever: the exe is only built on first launch, so a
  // fixed PetHost.cs would never reach anyone who already has one.
  let stale = false
  if (built) {
    try {
      stale = statSync(SRC).mtimeMs > statSync(EXE).mtimeMs
    } catch {
      stale = false
    }
  }
  if (built && !stale) return true

  if (!existsSync(csc)) {
    if (built) {
      log('PetHost.exe is older than PetHost.cs but csc.exe was not found; keeping the existing build')
      return true
    }
    log('PetHost.exe missing and csc.exe not found; cannot build the companion')
    return false
  }
  mkdirSync(dirname(EXE), { recursive: true })
  const args = [
    '/nologo', '/target:winexe', '/optimize+', `/out:${EXE}`,
    `/reference:${join(FX, 'WPF', 'PresentationFramework.dll')}`,
    `/reference:${join(FX, 'WPF', 'PresentationCore.dll')}`,
    `/reference:${join(FX, 'WPF', 'WindowsBase.dll')}`,
    `/reference:${join(FX, 'System.Xaml.dll')}`,
    `/reference:${join(FX, 'System.Web.Extensions.dll')}`,
    SRC,
  ]
  const result = spawnSync(csc, args, { windowsHide: true, encoding: 'utf8' })
  if (result.status !== 0) {
    const detail = (result.stderr || result.stdout || '').trim()
    // A running host locks its own exe. That is not a fatal error: the current process
    // keeps working and the next launch compiles the new source.
    if (/CS0016|cannot be opened|being used by another process/i.test(detail)) {
      log('companion rebuild skipped (PetHost.exe is in use); it will be rebuilt on the next launch')
      return built
    }
    log('companion build failed:', detail)
    return built && !stale
  }
  log('companion built:', EXE)
  return true
}

function readJson(path) {
  try { return JSON.parse(readFileSync(path, 'utf8')) } catch { return undefined }
}

function resolvePetDir(petsDir, petId) {
  for (const id of [petId, DEFAULT_PET]) {
    if (!id) continue
    const dir = join(petsDir, id)
    if (existsSync(join(dir, 'pet.json'))) return { id, dir }
  }
  try {
    for (const name of readdirSync(petsDir)) {
      const dir = join(petsDir, name)
      if (existsSync(join(dir, 'pet.json'))) return { id: name, dir }
    }
  } catch {}
  return undefined
}

function isAlive(pid) {
  if (!pid || pid < 1) return false
  try { process.kill(pid, 0); return true } catch { return false }
}

export function apply(ctx, config = {}) {
  if (process.platform !== 'win32') {
    log('the desktop companion currently supports Windows only')
    return
  }
  if (config.enabled === false) {
    log('disabled by config')
    return
  }
  if (!ensureCompanion()) return

  const petsDir = typeof config.petsDir === 'string' && config.petsDir ? config.petsDir : join(HERE, 'pets')
  const pet = resolvePetDir(petsDir, typeof config.petId === 'string' ? config.petId : DEFAULT_PET)
  if (!pet) {
    log('no pet pack found under', petsDir)
    return
  }

  const appData = process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local')
  const stateDir = join(appData, 'dsh-pet')
  const stateFile = join(stateDir, 'state.json')
  const pidFile = join(stateDir, 'pet.pid')
  const closedMarker = join(stateDir, 'pet.closed')
  const signalsFile = join(stateDir, 'signals.jsonl')
  const balanceFile = join(stateDir, 'balance.json')
  mkdirSync(stateDir, { recursive: true })

  // A new app/host process clears the previous session's "user closed the pet" marker.
  try { rmSync(closedMarker, { force: true }) } catch {}

  const baseArgs = [
    '--pet', pet.dir,
    '--state', stateFile,
    '--signals', signalsFile,
    '--parent-pid', String(process.pid),
  ]
  if (typeof config.scale === 'number' && config.scale > 0) baseArgs.push('--scale', String(config.scale))
  if (config.clickThrough === true) baseArgs.push('--click-through')
  if (config.sleep !== true) baseArgs.push('--no-sleep')
  if (config.showBalance !== false) baseArgs.push('--balance', balanceFile)
  if (config.windowAware === false) baseArgs.push('--no-window-aware')
  baseArgs.push('--walk-idle', String(typeof config.walkIdleSeconds === 'number' ? config.walkIdleSeconds : 60))

  // ---- platform balance -> balance.json --------------------------------
  ctx.effect(() => {
    if (config.showBalance === false) return () => {}
    let account
    try { account = typeof ctx.get === 'function' ? ctx.get('deepseekAccount') : undefined } catch { account = undefined }
    if (!account || typeof account.getBalance !== 'function') return () => {}
    let timer
    let stopped = false
    const refresh = async () => {
      if (stopped) return
      try {
        const client = {
          version: '0.2.0',
          locale: 'zh-CN',
          timezoneOffsetSeconds: -new Date().getTimezoneOffset() * 60,
        }
        const result = await account.getBalance(client)
        let payload
        if (result && result.status === 'ready') {
          payload = {
            status: 'ready',
            at: Date.now(),
            wallets: (result.value || []).map((w) => ({ currency: w.currency, balance: w.balance })),
            bonusWallets: (result.bonusWallets || []).map((w) => ({ currency: w.currency, balance: w.balance })),
          }
        } else {
          payload = { status: 'unavailable', at: Date.now() }
        }
        writeFileSync(balanceFile, JSON.stringify(payload), 'utf8')
      } catch (error) {
        try { writeFileSync(balanceFile, JSON.stringify({ status: 'error', at: Date.now() }), 'utf8') } catch {}
      }
    }
    refresh()
    timer = setInterval(refresh, 5 * 60 * 1000)
    return () => {
      stopped = true
      if (timer) clearInterval(timer)
    }
  })

  // ---- DSH events -> signals.jsonl -------------------------------------
  const react = config.reactToAgent !== false
  let signalWrites = 0
  const writeSignal = (type, extra) => {
    try {
      const payload = { type, at: Date.now() }
      if (extra) Object.assign(payload, extra)
      appendFileSync(signalsFile, JSON.stringify(payload) + '\n', 'utf8')
      signalWrites += 1
      if (signalWrites % 200 === 0) {
        try {
          if (statSync(signalsFile).size > 128 * 1024) {
            const lines = readFileSync(signalsFile, 'utf8').split('\n').filter(Boolean).slice(-60)
            writeFileSync(signalsFile, lines.join('\n') + '\n', 'utf8')
          }
        } catch {}
      }
    } catch {}
  }

  ctx.effect(() => {
    if (!react) return () => {}
    const disposers = []
    const on = (name, handler) => {
      try { disposers.push(ctx.on(name, handler)) } catch (error) { log('listen failed:', name, String(error)) }
    }

    // ---- one "task" = one agent turn; progress from tool calls ------------
    let task = { active: false, startedAt: 0, tools: 0 }
    const toolName = (exec) => {
      try {
        if (!exec) return ''
        return exec.name || exec.tool || exec.toolName || (exec.definition && exec.definition.name) || ''
      } catch { return '' }
    }
    const finishTask = () => {
      if (!task.active) return
      writeSignal('task.done', { tools: task.tools, durationMs: Date.now() - task.startedAt })
      task.active = false
    }
    // activating in the middle of a running turn: start counting from the first event
    const ensureTask = () => {
      if (task.active) return
      task = { active: true, startedAt: Date.now(), tools: 0 }
      writeSignal('task.start', { session: '' })
    }

    on('agent/status', (payload) => {
      if (!payload || !payload.status) return
      if (payload.status === 'running' && !task.active) {
        task = { active: true, startedAt: Date.now(), tools: 0 }
        writeSignal('task.start', { session: payload.sessionId || '' })
      } else if (payload.status === 'idle') {
        finishTask()
      }
      writeSignal('agent.status', { status: payload.status })
    })
    on('agent/assistant-stream', () => {
      ensureTask()
      const now = Date.now()
      if (now - (writeSignal.lastStream || 0) < 250) return
      writeSignal.lastStream = now
      writeSignal('assistant.stream')
    })
    on('tools/result', (exec, result) => {
      ensureTask()
      let ok = true
      try {
        if (result && (result.ok === false || result.isError === true || result.error)) ok = false
      } catch {}
      task.tools += 1
      writeSignal('task.progress', { tools: task.tools, tool: toolName(exec) })
      writeSignal('tool.result', { ok })
    })
    on('agent/turn-stopping', () => {
      finishTask()
      writeSignal('turn.end')
    })
    on('agent/error', () => {
      if (task.active) {
        writeSignal('task.failed', { tools: task.tools })
        task.active = false
      }
      writeSignal('agent.error')
    })
    on('goal/changed', () => writeSignal('goal.changed'))

    return () => {
      for (const dispose of disposers) {
        try { dispose() } catch {}
      }
    }
  })

  // ---- companion process lifecycle ------------------------------------
  ctx.effect(() => {
    let child
    let stopped = false
    const spawnLog = join(stateDir, 'spawn.log')
    const note = (text) => {
      try { appendFileSync(spawnLog, text + '\n', 'utf8') } catch {}
    }

    const ensure = () => {
      if (stopped) return
      if (existsSync(closedMarker)) return

      const previous = readJson(pidFile)
      if (previous && previous.parentPid === process.pid && isAlive(previous.pid)) return
      if (previous && isAlive(previous.pid)) {
        try { process.kill(previous.pid) } catch {}
      }

      try {
        child = spawn(EXE, baseArgs, { cwd: HERE, detached: false, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
        child.stdout.on('data', (chunk) => note(String(chunk)))
        child.stderr.on('data', (chunk) => note(String(chunk)))
        child.on('error', (error) => note('spawn error: ' + error.message))
        child.on('exit', (code) => note('exit code ' + code + ' at ' + new Date().toISOString()))
        writeFileSync(pidFile, JSON.stringify({
          pid: child.pid, petId: pet.id, parentPid: process.pid, startedAt: Date.now(),
        }))
        note('spawned pid ' + child.pid + ' exe=' + EXE + ' pet=' + pet.dir + ' signals=' + signalsFile)
      } catch (error) {
        note('start failed: ' + (error instanceof Error ? error.message : String(error)))
      }
    }

    ensure()
    const watchdog = setInterval(ensure, 10000)
    return () => {
      stopped = true
      clearInterval(watchdog)
      if (child && isAlive(child.pid)) {
        try { process.kill(child.pid) } catch {}
      }
    }
  })
}





