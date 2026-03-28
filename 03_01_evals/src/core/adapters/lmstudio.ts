import OpenAI from 'openai'
import {
  APIConnectionError,
  APIConnectionTimeoutError,
  AuthenticationError,
  BadRequestError,
  InternalServerError,
  RateLimitError,
} from 'openai/error'
import type { Adapter, CompletionError, CompletionResult, Message, ToolCall } from '../../types.js'
import type { Logger } from '../logger.js'
import { err, ok } from '../result.js'

interface LMStudioAdapterConfig {
  defaultModel: string
  baseURL?: string
  logger: Logger
}

const DEFAULT_BASE_URL = 'http://localhost:1234/v1'

const mapError = (error: unknown): CompletionError => {
  if (error instanceof AuthenticationError) {
    return { code: 'AUTHENTICATION_ERROR', message: error.message, provider: 'lmstudio', status: error.status }
  }

  if (error instanceof RateLimitError) {
    return { code: 'RATE_LIMITED', message: error.message, provider: 'lmstudio', status: error.status }
  }

  if (error instanceof BadRequestError) {
    return { code: 'BAD_REQUEST', message: error.message, provider: 'lmstudio', status: error.status }
  }

  if (error instanceof APIConnectionTimeoutError) {
    return { code: 'TIMEOUT', message: error.message, provider: 'lmstudio' }
  }

  if (error instanceof APIConnectionError) {
    return { code: 'CONNECTION_ERROR', message: error.message, provider: 'lmstudio' }
  }

  if (error instanceof InternalServerError) {
    return { code: 'INTERNAL_SERVER_ERROR', message: error.message, provider: 'lmstudio', status: error.status }
  }

  return {
    code: 'UNKNOWN_ERROR',
    message: error instanceof Error ? error.message : String(error),
    provider: 'lmstudio',
  }
}

// Translate Responses API input items -> Chat Completions messages
const toMessages = (input: Message[], instructions?: string): OpenAI.ChatCompletionMessageParam[] => {
  const messages: OpenAI.ChatCompletionMessageParam[] = []

  if (instructions) {
    messages.push({ role: 'system', content: instructions })
  }

  for (const item of input) {
    if ('role' in item && item.role === 'user') {
      const content = typeof item.content === 'string' ? item.content : JSON.stringify(item.content)
      messages.push({ role: 'user', content })
    } else if (item.type === 'message' && item.role === 'assistant') {
      const content = item.content
      let text = ''
      if (Array.isArray(content)) {
        for (const b of content as Array<{ type: string; text?: string }>) {
          if (b.type === 'output_text' && b.text) text += b.text
        }
      } else {
        text = String(content)
      }
      messages.push({ role: 'assistant', content: text })
    } else if (item.type === 'function_call') {
      messages.push({
        role: 'assistant',
        content: null,
        tool_calls: [
          {
            id: item.call_id,
            type: 'function',
            function: { name: item.name, arguments: item.arguments },
          },
        ],
      })
    } else if (item.type === 'function_call_output') {
      const output = typeof item.output === 'string' ? item.output : JSON.stringify(item.output)
      messages.push({ role: 'tool', tool_call_id: item.call_id, content: output })
    }
  }

  return messages
}

// Translate Responses API FunctionTool[] -> Chat Completions tools
const toTools = (
  tools?: OpenAI.Responses.FunctionTool[],
): OpenAI.ChatCompletionTool[] | undefined => {
  if (!tools || tools.length === 0) return undefined

  return tools.map((tool) => ({
    type: 'function' as const,
    function: {
      name: tool.name,
      ...(tool.description != null ? { description: tool.description } : {}),
      parameters: tool.parameters as Record<string, unknown>,
    },
  }))
}

// Translate Chat Completions response message -> Responses API OutputItem[]
const toOutputItems = (message: OpenAI.Chat.Completions.ChatCompletionMessage): OpenAI.Responses.ResponseOutputItem[] => {
  const items: OpenAI.Responses.ResponseOutputItem[] = []

  if (message.tool_calls && message.tool_calls.length > 0) {
    for (const tc of message.tool_calls) {
      const fn = tc.type === 'function' ? tc.function : null
      if (!fn) continue
      items.push({
        type: 'function_call',
        id: tc.id,
        call_id: tc.id,
        name: fn.name,
        arguments: fn.arguments,
        status: 'completed',
      } as OpenAI.Responses.ResponseFunctionToolCall)
    }
  } else {
    items.push({
      type: 'message',
      id: '',
      role: 'assistant',
      content: [{ type: 'output_text', text: message.content ?? '', annotations: [] }],
      status: 'completed',
    } as OpenAI.Responses.ResponseOutputMessage)
  }

  return items
}

const extractText = (output: OpenAI.Responses.ResponseOutputItem[]): string => {
  for (const item of output) {
    if (item.type === 'message') {
      for (const block of item.content) {
        if (block.type === 'output_text') return block.text
      }
    }
  }
  return ''
}

const extractToolCalls = (output: OpenAI.Responses.ResponseOutputItem[]): ToolCall[] =>
  output
    .filter((item): item is OpenAI.Responses.ResponseFunctionToolCall => item.type === 'function_call')
    .map((item) => ({ callId: item.call_id, name: item.name, arguments: item.arguments }))

export const lmstudioAdapter = (config: LMStudioAdapterConfig): Adapter => {
  const client = new OpenAI({
    apiKey: 'lm-studio',
    baseURL: config.baseURL ?? DEFAULT_BASE_URL,
  })
  const model = config.defaultModel
  const log = config.logger.child({ module: 'lmstudio-adapter' })

  return {
    complete: async (params) => {
      try {
        const response = await client.chat.completions.create({
          model: params.model ?? model,
          messages: toMessages(params.input, params.instructions),
          tools: toTools(params.tools),
        })

        const message = response.choices[0]?.message
        if (!message) {
          return err({ code: 'UNKNOWN_ERROR', message: 'No choices returned from LM Studio', provider: 'lmstudio' })
        }

        const output = toOutputItems(message)
        const usage = response.usage

        const value: CompletionResult = {
          text: extractText(output),
          toolCalls: extractToolCalls(output),
          output,
          usage: usage
            ? { input: usage.prompt_tokens, output: usage.completion_tokens, total: usage.total_tokens }
            : undefined,
        }

        log.info('Completion successful', {
          hasText: value.text.length > 0,
          toolCalls: value.toolCalls.length,
        })

        return ok(value)
      } catch (error) {
        const mapped = mapError(error)
        log.error('Completion failed', { code: mapped.code, message: mapped.message })
        return err(mapped)
      }
    },
  }
}
