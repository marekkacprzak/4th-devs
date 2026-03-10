export type RawToolCall = { type: string; arguments: string; name: string; call_id?: string };
export type ToolCall = { arguments: string; name: string; call_id?: string };
export type HandlerFn = (args: unknown) => unknown | Promise<unknown>;

export const getToolCalls = (response: unknown): RawToolCall[] => {
  if (typeof response !== "object" || response === null) return [];
  const out = (response as Record<string, unknown>)['output'];
  if (!Array.isArray(out)) return [];
  return out
    .filter((item): item is Record<string, unknown> => {
      if (typeof item !== 'object' || item === null) return false;
      const t = (item as Record<string, unknown>)['type'];
      return typeof t === 'string' && t === 'function_call';
    })
    .map((item) => {
      const rec = item as Record<string, unknown>;
      const args = typeof rec['arguments'] === 'string' ? (rec['arguments'] as string) : JSON.stringify(rec['arguments'] ?? '');
      const name = typeof rec['name'] === 'string' ? (rec['name'] as string) : String(rec['name'] ?? '');
      const call_id = typeof rec['call_id'] === 'string' ? (rec['call_id'] as string) : undefined;
      const type = typeof rec['type'] === 'string' ? (rec['type'] as string) : 'function_call';
      return { type, arguments: args, name, call_id } as RawToolCall;
    });
};

export const getFinalText = (response: unknown): string => {
  if (typeof response !== 'object' || response === null) return 'No response';
  const resp = response as Record<string, unknown>;
  if (typeof resp.output_text === 'string') return resp.output_text;
  const msg = Array.isArray(resp.output)
    ? resp.output.find((item) => {
      if (typeof item !== 'object' || item === null) return false;
      const t = (item as Record<string, unknown>)['type'];
      return typeof t === 'string' && t === 'message';
    }) as Record<string, unknown> | undefined
    : undefined;
  const text = msg?.content && Array.isArray(msg.content) ? (msg.content[0] as Record<string, unknown>)['text'] : undefined;
  return typeof text === 'string' ? text : 'No response';
};

const supportsColor = Boolean(process.stdout.isTTY && !process.env.NO_COLOR);

const ansi: Record<string, string> = {
  reset: "\x1b[0m",
  bold: "\x1b[1m",
  dim: "\x1b[2m",
  blue: "\x1b[34m",
  cyan: "\x1b[36m",
  green: "\x1b[32m",
  magenta: "\x1b[35m",
  yellow: "\x1b[33m",
};

const colorize = (text: string, ...styles: string[]) => {
  if (!supportsColor) {
    return text;
  }

  const sequence = styles.map((style) => ansi[style]).join("");
  return `${sequence}${text}${ansi.reset}`;
};

const label = (text: string, color: string) => colorize(`[${text}]`, "bold", color);
const formatJson = (value: unknown) => JSON.stringify(value, null, 2);

export const logQuestion = (text: string) => {
  console.log(`${label("USER", "blue")} ${text}\n`);
};

export const logToolCall = (name: string, args: unknown) => {
  console.log(`${label("TOOL", "magenta")} ${colorize(name, "bold")}`);
  console.log(colorize("Arguments:", "cyan"));
  console.log(colorize(formatJson(args), "dim"));
};

export const logToolResult = (result: unknown) => {
  console.log(colorize("Result:", "yellow"));
  console.log(colorize(formatJson(result), "dim"));
  console.log("");
};

export const logAnswer = (text: string) => {
  console.log(`${label("ASSISTANT", "green")} ${text}`);
};

export const executeToolCall = async (call: RawToolCall, handlers: Record<string, HandlerFn>) => {
  const args = JSON.parse(call.arguments) as unknown;
  const handler = handlers[call.name];

  if (!handler) {
    throw new Error(`Unknown tool: ${call.name}`);
  }

  logToolCall(call.name, args);
  const result = await handler(args);
  logToolResult(result);

  return {
    type: "function_call_output",
    call_id: call.call_id,
    output: JSON.stringify(result),
  };
};

export const buildNextConversation = async (conversation: unknown[], toolCalls: RawToolCall[], handlers: Record<string, HandlerFn>) => {
  const toolResults = await Promise.all(
    toolCalls.map((call) => executeToolCall(call, handlers)),
  );

  return [...conversation, ...toolCalls, ...toolResults];
};
