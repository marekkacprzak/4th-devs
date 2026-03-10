import {
  AI_API_KEY,
  buildResponsesRequest,
  EXTRA_API_HEADERS,
  RESPONSES_API_ENDPOINT,
  resolveModelForProvider,
} from "../config.ts";
import { AnyData } from "../types.ts";
import {
  buildNextConversation,
  getFinalText,
  getToolCalls,
  logAnswer,
  logQuestion,
} from "./helper.ts";

const model = resolveModelForProvider("gpt-4.1-mini");

const webSearch = true;

type ToolDefinition = {
  type: string;
  name: string;
  description: string;
  parameters: AnyData;
  strict?: boolean;
};

const tools: ToolDefinition[] = [
  {
    type: "function",
    name: "get_weather",
    description: "Get current weather for a given location",
    parameters: {
      type: "object",
      properties: {
        location: { type: "string", description: "City name" },
      },
      required: ["location"],
      additionalProperties: false,
    },
    strict: true,
  },
  {
    type: "function",
    name: "send_email",
    description: "Send a short email message to a recipient",
    parameters: {
      type: "object",
      properties: {
        to: { type: "string", description: "Recipient email address" },
        subject: { type: "string", description: "Email subject" },
        body: { type: "string", description: "Plain-text email body" },
      },
      required: ["to", "subject", "body"],
      additionalProperties: false,
    },
    strict: true,
  },
];

const requireText = (value: AnyData, fieldName: string) => {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`"${fieldName}" must be a non-empty string.`);
  }

  return value.trim();
};

type HandlerResult = AnyData;
type Handlers = Record<string, (args: AnyData) => HandlerResult | Promise<HandlerResult>>;

const handlers: Handlers = {
  get_weather({ location }: { location: string }) {
    const city = requireText(location, "location");
    const weather: Record<string, { temp: number | null; conditions: string }> = {
      "Kraków": { temp: -2, conditions: "snow" },
      "London": { temp: 8, conditions: "rain" },
      "Tokyo": { temp: 15, conditions: "cloudy" },
    };
    return weather[city] ?? { temp: null, conditions: "unknown" };
  },

  send_email({ to, subject, body }: { to: string; subject: string; body: string }) {
    const recipient = requireText(to, "to");
    const emailSubject = requireText(subject, "subject");
    const emailBody = requireText(body, "body");

    return {
      success: true,
      status: "sent",
      to: recipient,
      subject: emailSubject,
      body: emailBody,
    };
  },
};

const requestResponse = async (input: AnyData) => {
  const body = buildResponsesRequest({
    model,
    input,
    tools,
    plugins: undefined,
    webSearch,
  });

  const response = await fetch(RESPONSES_API_ENDPOINT, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${AI_API_KEY}`,
      ...EXTRA_API_HEADERS,
    },
    body: JSON.stringify(body),
  });

  const data = await response.json();
  if (!response.ok) throw new Error(data?.error?.message ?? `Request failed (${response.status})`);
  return data;
};

const MAX_TOOL_STEPS = 5;

const chat = async (conversation: AnyData[]) => {
  let currentConversation = conversation;
  let stepsRemaining = MAX_TOOL_STEPS;

  while (stepsRemaining > 0) {
    stepsRemaining -= 1;

    const response = await requestResponse(currentConversation);
    const toolCalls = getToolCalls(response);

    if (toolCalls.length === 0) {
      return getFinalText(response);
    }

    currentConversation = await buildNextConversation(currentConversation, toolCalls, handlers);
  }

  throw new Error(`Tool calling did not finish within ${MAX_TOOL_STEPS} steps.`);
};

const query = "Use web search to check the current weather in Kraków. Then send a short email with the answer to student@example.com.";
logQuestion(query);

const answer = await chat([{ role: "user", content: query }]);
logAnswer(answer);
