import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { AnyData, AnyObject } from "./types";

const MIN_NODE_VERSION = 24;
const ROOT_DIR = path.dirname(fileURLToPath(import.meta.url));
const ROOT_ENV_FILE = path.join(ROOT_DIR, ".env");
const RESPONSES_ENDPOINTS: Record<string, string> = {
  openai: "https://api.openai.com/v1/responses",
  openrouter: "https://openrouter.ai/api/v1/responses",
};
const EMBEDDINGS_ENDPOINTS: Record<string, string> = {
  openai: "https://api.openai.com/v1/embeddings",
  openrouter: "https://openrouter.ai/api/v1/embeddings",
};
const CHAT_API_BASE_URLS: Record<string, string> = {
  openai: "https://api.openai.com/v1",
  openrouter: "https://openrouter.ai/api/v1",
};
const OPENROUTER_ONLINE_SUFFIX = ":online";
const VALID_OPENAI_SEARCH_CONTEXT_SIZES = new Set(["low", "medium", "high"]);
const VALID_OPENROUTER_WEB_ENGINES = new Set(["native", "exa"]);
const VALID_PROVIDERS = new Set(["openai", "openrouter"]);

const [major] = process.versions.node.split(".").map(Number);
if (major < MIN_NODE_VERSION) {
  console.error("\x1b[31mError: Node.js ${MIN_NODE_VERSION}+ is required\x1b[0m");
  console.error(`       Current version: ${process.versions.node}`);
  console.error("       Please upgrade: https://nodejs.org/");
  process.exit(1);
}


const procWithLoader = process as unknown as { loadEnvFile?: (path: string) => void };
if (existsSync(ROOT_ENV_FILE) && typeof procWithLoader.loadEnvFile === "function") {
  try {
    procWithLoader.loadEnvFile!(ROOT_ENV_FILE);
  } catch (error) {
    const err = error instanceof Error ? error : { message: String(error) };
    console.error("\x1b[31mError: Failed to load .env file\x1b[0m");
    console.error(`       File: ${ROOT_ENV_FILE}`);
    console.error(`       Reason: ${err.message}`);
    process.exit(1);
  }
}

export const OPENAI_API_KEY = (process.env.OPENAI_API_KEY ?? "").toString().trim();
export const OPENROUTER_API_KEY = (process.env.OPENROUTER_API_KEY ?? "").toString().trim();
const requestedProvider = (process.env.AI_PROVIDER ?? "").toString().trim().toLowerCase();
const hasOpenAIKey = Boolean(OPENAI_API_KEY);
const hasOpenRouterKey = Boolean(OPENROUTER_API_KEY);

export const LLMSTUDIO_MODEL = (process.env.LLMSTUDIO_MODEL ?? "").toString().trim();

if (!hasOpenAIKey && !hasOpenRouterKey && !LLMSTUDIO_MODEL) {
  console.error("\x1b[31mError: API key is not set\x1b[0m");
  console.error(`       Create: ${ROOT_ENV_FILE}`);
  console.error("       Add one of:");
  console.error("       OPENAI_API_KEY=sk-...");
  console.error("       OPENROUTER_API_KEY=sk-or-v1-...");
  console.error("       LLMSTUDIO_MODEL=<model-name>  (for local LM Studio)");
  process.exit(1);
}

if (requestedProvider && !VALID_PROVIDERS.has(requestedProvider)) {
  console.error("\x1b[31mError: AI_PROVIDER must be one of: openai, openrouter\x1b[0m");
  process.exit(1);
}

const resolveProvider = (): string => {
  if (requestedProvider) {
    if (requestedProvider === "openai" && !hasOpenAIKey) {
      console.error("\x1b[31mError: AI_PROVIDER=openai requires OPENAI_API_KEY\x1b[0m");
      process.exit(1);
    }

    if (requestedProvider === "openrouter" && !hasOpenRouterKey) {
      console.error("\x1b[31mError: AI_PROVIDER=openrouter requires OPENROUTER_API_KEY\x1b[0m");
      process.exit(1);
    }

    return requestedProvider;
  }

  return hasOpenAIKey ? "openai" : "openrouter";
};

export const AI_PROVIDER = resolveProvider();
export const AI_API_KEY = AI_PROVIDER === "openai" ? OPENAI_API_KEY : OPENROUTER_API_KEY;
export const RESPONSES_API_ENDPOINT = RESPONSES_ENDPOINTS[AI_PROVIDER];
export const EMBEDDINGS_API_ENDPOINT = EMBEDDINGS_ENDPOINTS[AI_PROVIDER];
export const CHAT_API_BASE_URL = CHAT_API_BASE_URLS[AI_PROVIDER];
export const OPENROUTER_EXTRA_HEADERS: Record<string, string> = {
  ...(process.env.OPENROUTER_HTTP_REFERER ? { "HTTP-Referer": process.env.OPENROUTER_HTTP_REFERER as string } : {}),
  ...(process.env.OPENROUTER_APP_NAME ? { "X-Title": process.env.OPENROUTER_APP_NAME as string } : {}),
};
export const EXTRA_API_HEADERS = AI_PROVIDER === "openrouter" ? OPENROUTER_EXTRA_HEADERS : {};

const isPlainObject = (value: AnyData): value is AnyObject =>
  value !== null && typeof value === "object" && !Array.isArray(value);

const ensureTrimmedString = (value: AnyData, fieldName: string): string => {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`${fieldName} must be a non-empty string`);
  }

  return value.trim();
};

const normalizeOpenRouterOnlineModel = (model: string) =>
  model.endsWith(OPENROUTER_ONLINE_SUFFIX) ? model : `${model}${OPENROUTER_ONLINE_SUFFIX}`;

const stripOpenRouterOnlineSuffix = (model: string) =>
  model.endsWith(OPENROUTER_ONLINE_SUFFIX) ? model.slice(0, -OPENROUTER_ONLINE_SUFFIX.length) : model;

export const resolveModelForProvider = (model: string) => {
  if (typeof model !== "string" || !model.trim()) {
    throw new Error("Model must be a non-empty string");
  }

  if (AI_PROVIDER !== "openrouter" || model.includes("/")) {
    return model;
  }

  return model.startsWith("gpt-") ? `openai/${model}` : model;
};

type WebSearchConfig = {
  enabled?: boolean;
  searchContextSize?: string;
  engine?: string;
  maxResults?: number;
  searchPrompt?: string;
};

const normalizeWebSearchConfig = (webSearch: boolean | WebSearchConfig | undefined): WebSearchConfig | null => {
  if (!webSearch) {
    return null;
  }

  if (webSearch === true) {
    return {};
  }

  if (!isPlainObject(webSearch)) {
    throw new Error("webSearch must be either boolean or an object");
  }

  if ((webSearch as WebSearchConfig).enabled === false) {
    return null;
  }

  const config: WebSearchConfig = {};

  if ((webSearch as WebSearchConfig).searchContextSize !== undefined) {
    const searchContextSize = ensureTrimmedString((webSearch as WebSearchConfig).searchContextSize, "webSearch.searchContextSize");

    if (!VALID_OPENAI_SEARCH_CONTEXT_SIZES.has(searchContextSize)) {
      throw new Error('webSearch.searchContextSize must be one of: "low", "medium", "high"');
    }

    config.searchContextSize = searchContextSize;
  }

  if ((webSearch as WebSearchConfig).engine !== undefined) {
    const engine = ensureTrimmedString((webSearch as WebSearchConfig).engine, "webSearch.engine");

    if (!VALID_OPENROUTER_WEB_ENGINES.has(engine)) {
      throw new Error('webSearch.engine must be one of: "native", "exa"');
    }

    config.engine = engine;
  }

  if ((webSearch as WebSearchConfig).maxResults !== undefined) {
    if (!Number.isInteger((webSearch as WebSearchConfig).maxResults) || (webSearch as WebSearchConfig).maxResults! <= 0) {
      throw new Error("webSearch.maxResults must be a positive integer");
    }

    config.maxResults = (webSearch as WebSearchConfig).maxResults;
  }

  if ((webSearch as WebSearchConfig).searchPrompt !== undefined) {
    config.searchPrompt = ensureTrimmedString((webSearch as WebSearchConfig).searchPrompt, "webSearch.searchPrompt");
  }

  return config;
};


const addUniqueTool = (tools: AnyData, tool: AnyData) => {
  if (!Array.isArray(tools) || tools.length === 0) {
    return [tool];
  }
  const arr = tools as Array<AnyObject>;
  const toolRec = typeof tool === 'object' && tool !== null ? tool as AnyObject : undefined;
  const toolType = toolRec ? toolRec['type'] : undefined;
  if (!toolType) return arr;
  const exists = arr.some((candidate) => typeof candidate === 'object' && candidate !== null && (candidate as AnyObject)['type'] === toolType);
  return exists ? arr : [...arr, tool];
};


const mergeOpenRouterPlugins = (plugins: AnyData, plugin: AnyObject) => {
  if (!Array.isArray(plugins) || plugins.length === 0) {
    return [plugin];
  }
  const arr = plugins as Array<AnyObject>;
  const pluginId = plugin['id'];
  const existingIndex = arr.findIndex((candidate) => typeof candidate === 'object' && candidate !== null && (candidate as AnyObject)['id'] === pluginId);

  if (existingIndex === -1) {
    return [...arr, plugin];
  }

  const mergedPlugin = { ...arr[existingIndex], ...plugin };
  return arr.map((candidate, index) => (index === existingIndex ? mergedPlugin : candidate));
};

export type BuildResponsesRequestArgs = {
  model: string;
  tools?: AnyData;
  plugins?: AnyData;
  webSearch?: boolean | WebSearchConfig;
  [key: string]: AnyData;
};

export const buildResponsesRequest = ({ model, tools, plugins, webSearch = false, ...rest }: BuildResponsesRequestArgs) => {
  const request: AnyObject = {
    model: resolveModelForProvider(model),
    ...rest,
  };

  if (tools) {
    request.tools = tools;
  }

  if (plugins) {
    request.plugins = plugins;
  }

  const webSearchConfig = normalizeWebSearchConfig(webSearch);

  if (!webSearchConfig) {
    return request;
  }

  if (AI_PROVIDER === "openrouter") {
    const hasPluginOverrides = webSearchConfig.engine !== undefined || webSearchConfig.maxResults !== undefined || webSearchConfig.searchPrompt !== undefined;

    if (!hasPluginOverrides) {
      request.model = normalizeOpenRouterOnlineModel(request.model as string);
      return request;
    }

    request.model = stripOpenRouterOnlineSuffix(request.model as string);
    request.plugins = mergeOpenRouterPlugins(request.plugins, {
      id: "web",
      ...(webSearchConfig.engine ? { engine: webSearchConfig.engine } : {}),
      ...(webSearchConfig.maxResults ? { max_results: webSearchConfig.maxResults } : {}),
      ...(webSearchConfig.searchPrompt ? { search_prompt: webSearchConfig.searchPrompt } : {}),
    });

    return request;
  }

  request.tools = addUniqueTool(request.tools, { type: "web_search_preview" });

  if (webSearchConfig.searchContextSize) {
    request.web_search_options = { search_context_size: webSearchConfig.searchContextSize };
  }

  return request;
};

// Backward-compatible alias used in existing examples.
// (individual constants already exported above)
