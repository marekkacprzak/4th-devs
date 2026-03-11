import {
  AI_API_KEY,
  EXTRA_API_HEADERS,
  RESPONSES_API_ENDPOINT,
  resolveModelForProvider
} from "../config.ts";
import { extractResponseText, toMessage } from "./helpers.ts";
import type { ErrorLike, ChatResult, Message, ResponseObject } from "./types.ts";

const MODEL = resolveModelForProvider("gpt-5.2");

export async function chat(input: string, history: Message[] = []): Promise<ChatResult> {
  const response = await fetch(RESPONSES_API_ENDPOINT, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Authorization": `Bearer ${AI_API_KEY}`,
      ...EXTRA_API_HEADERS
    },
    body: JSON.stringify({
      model: MODEL,
      input: [...history, toMessage("user", input)],
      reasoning: { effort: "medium" }
    })
  });

  const data = (await response.json()) as ResponseObject;
  //logStructure("responses_api.data", data);

  if (!response.ok || data.error) {
    const message = data?.error?.message ?? `Request failed with status ${response.status}`;
    throw new Error(message);
  }

  const text = extractResponseText(data);

  if (!text) {
    throw new Error("Missing text output in API response");
  }

  return {
    text,
    reasoningTokens: (data?.usage?.output_tokens_details?.reasoning_tokens as number) ?? 0
  };
}

async function main(): Promise<void> {
  const firstQuestion = "What is 25 * 48?";
  const firstAnswer = await chat(firstQuestion);

  const secondQuestion = "Divide that by 4.";
  const secondQuestionContext: Message[] = [
    {
      type: "message",
      role: "user",
      content: firstQuestion
    },
    {
      type: "message",
      role: "assistant",
      content: firstAnswer.text
    }
  ];
  const secondAnswer = await chat(secondQuestion, secondQuestionContext);

  console.log("Q:", firstQuestion);
  console.log("A:", firstAnswer.text, `(${firstAnswer.reasoningTokens} reasoning tokens)`);
  console.log("Q:", secondQuestion);
  console.log("A:", secondAnswer.text, `(${secondAnswer.reasoningTokens} reasoning tokens)`);
}

main().catch((error: ErrorLike) => {
  //logStructure("main.error", error);
  console.error(`Error: ${error?.message}`);
  process.exit(1);
});
