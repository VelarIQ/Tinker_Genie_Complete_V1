/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL: string
  readonly VITE_SIGNALR_URL: string
  // Add other env variables as needed
}

interface ImportMeta {
  readonly env: ImportMetaEnv
  readonly hot?: {
    accept: (callback?: () => void) => void
    dispose: (callback: () => void) => void
  }
}

// Extend ServiceWorkerRegistration
interface SyncManager {
  register(tag: string): Promise<void>
  getTags(): Promise<string[]>
}

interface ServiceWorkerRegistration {
  readonly sync?: SyncManager
}

// Extend Screen interface
interface ScreenOrientation {
  lock(orientation: OrientationLockType): Promise<void>
  unlock(): void
  readonly type: OrientationType
  readonly angle: number
  addEventListener(type: string, listener: EventListener): void
  removeEventListener(type: string, listener: EventListener): void
}

interface Screen {
  readonly orientation?: ScreenOrientation
}
