/**
 * AI integration module - handles OpenAI API communication
 */

import "dotenv/config";

const OPENAI_API_ENDPOINT = "https://api.openai.com/v1/responses";
const OPENAI_API_KEY = process.env.OPENAI_API_KEY;

if (!OPENAI_API_KEY) {
  throw new Error("OPENAI_API_KEY not found in environment variables");
}

/**
 * Sends a chat request to OpenAI
 * @param {object} config
 * @param {string} config.model - Model identifier
 * @param {Array} config.input - Conversation messages
 * @param {Array} config.tools - Tool definitions
 * @param {string} config.instructions - System instructions
 * @returns {Promise<object>} API response
 */
export const chat = async ({ model, input, tools, instructions }) => {
  const body = { 
    model, 
    input,
    ...(tools?.length && { tools, tool_choice: "auto" }),
    ...(instructions && { instructions })
  };

  const response = await fetch(OPENAI_API_ENDPOINT, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Authorization": `Bearer ${OPENAI_API_KEY}`
    },
    body: JSON.stringify(body)
  });

  const data = await response.json();

  if (!response.ok || data.error) {
    throw new Error(data?.error?.message || `API request failed (${response.status})`);
  }

  return data;
};

/**
 * Extracts tool calls from the API response
 */
export const extractToolCalls = (response) =>
  (response.output ?? []).filter((item) => item.type === "function_call");

/**
 * Extracts text response from the API response
 */
export const extractText = (response) => {
  if (typeof response?.output_text === "string") {
    return response.output_text.trim();
  }

  const message = response?.output?.find((o) => o?.type === "message");
  const part = message?.content?.find((c) => c?.type === "output_text");
  return part?.text?.trim() ?? "";
};
