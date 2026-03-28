import type { Adapter, AdapterResolver, CompletionError, Provider } from '../../types.js'
import type { Logger } from '../logger.js'
import { err, ok } from '../result.js'
import { withGenerationTracing } from '../tracing/index.js'
import { lmstudioAdapter } from './lmstudio.js'
import { openaiAdapter } from './openai.js'

interface AdapterConfig {
  apiKey: string
  baseURL?: string
  defaultHeaders?: Record<string, string>
}

interface LMStudioConfig {
  defaultModel: string
  baseURL?: string
}

interface AdaptersConfig {
  openai?: AdapterConfig
  lmstudio?: LMStudioConfig
  logger: Logger
  enableTracing?: boolean
}

export const adapters = (config: AdaptersConfig): AdapterResolver => {
  const log = config.logger.child({ module: 'adapters' })
  const enableTracing = config.enableTracing ?? true

  const maybeTrace = (adapter: Adapter): Adapter => {
    return enableTracing ? withGenerationTracing(adapter) : adapter
  }

  const registry: Partial<Record<Provider, Adapter>> = {}

  if (config.openai) {
    registry.openai = maybeTrace(openaiAdapter({
      apiKey: config.openai.apiKey,
      baseURL: config.openai.baseURL,
      defaultHeaders: config.openai.defaultHeaders,
      logger: log,
    }))
  }

  if (config.lmstudio) {
    registry.lmstudio = maybeTrace(lmstudioAdapter({
      defaultModel: config.lmstudio.defaultModel,
      baseURL: config.lmstudio.baseURL,
      logger: log,
    }))
  }

  return (provider: Provider) => {
    const adapter = registry[provider]

    if (!adapter) {
      return err<CompletionError>({
        code: 'PROVIDER_NOT_CONFIGURED',
        message: `Provider "${provider}" not configured`,
        provider,
      })
    }

    return ok(adapter)
  }
}
