/**
 * Logistics Assistant REST API Server
 * 
 * Mission: Assist logistics operator while redirecting reactor parts package
 * to PWR6132PL and obtaining the secret flag code.
 */

import express from "express";
import { chat, extractToolCalls, extractText } from "./src/ai.js";
import { tools, handlers } from "./src/tools.js";
import { SYSTEM_PROMPT } from "./src/prompts.js";
import {
  getSession,
  addUserMessage,
  addAssistantOutput,
  addToolResults
} from "./src/session.js";

const app = express();
const PORT = process.env.PORT || 3000;
const MODEL = "gpt-4o-mini";
const MAX_TOOL_ROUNDS = 10;

// Middleware
app.use(express.json());

/**
 * Execute a single tool call
 */
const executeToolCall = async (call) => {
  const args = JSON.parse(call.arguments);
  const handler = handlers[call.name];

  if (!handler) {
    throw new Error(`Unknown tool: ${call.name}`);
  }

  console.log(`[Tool] ${call.name}(${JSON.stringify(args)})`);

  try {
    const result = await handler(args);
    console.log(`[Result] ${JSON.stringify(result)}`);
    return {
      type: "function_call_output",
      call_id: call.call_id,
      output: JSON.stringify(result)
    };
  } catch (error) {
    console.error(`[Error] ${error.message}`);
    return {
      type: "function_call_output",
      call_id: call.call_id,
      output: JSON.stringify({ error: error.message })
    };
  }
};

/**
 * Check if message contains the flag code
 */
const extractFlag = (message) => {
  const flagMatch = message.match(/\{FLG:([A-Z0-9]+)\}/i);
  if (flagMatch) {
    return flagMatch[1];
  }
  return null;
};

/**
 * Process a message and generate response
 */
const processMessage = async (sessionID, message) => {
  console.log(`\n[Session: ${sessionID}]`);
  console.log(`[User] ${message}`);

  // Check for flag in user message
  const flag = extractFlag(message);
  if (flag) {
    console.log(`[FLAG FOUND!] ${flag}`);
  }

  // Add user message to conversation history
  addUserMessage(sessionID, message);
  const conversation = getSession(sessionID);

  // Agent loop with tool calling
  for (let round = 0; round < MAX_TOOL_ROUNDS; round++) {
    const response = await chat({
      model: MODEL,
      input: conversation,
      tools,
      instructions: SYSTEM_PROMPT
    });

    const toolCalls = extractToolCalls(response);

    // No tool calls - return the text response
    if (toolCalls.length === 0) {
      const text = extractText(response) || "Przepraszam, nie mogę teraz odpowiedzieć.";
      
      // Save assistant response to history
      addAssistantOutput(sessionID, response.output);
      
      console.log(`[Assistant] ${text}`);
      
      // Check for flag in assistant response too
      const assistantFlag = extractFlag(text);
      if (assistantFlag) {
        console.log(`[FLAG FOUND IN RESPONSE!] ${assistantFlag}`);
      }
      
      return { msg: text };
    }

    // Execute tool calls
    console.log(`[Tool Calls] ${toolCalls.length} tool(s) to execute`);
    
    // Save assistant output with tool calls
    addAssistantOutput(sessionID, response.output);
    
    // Execute all tool calls in parallel
    const toolResults = await Promise.all(
      toolCalls.map((call) => executeToolCall(call))
    );
    
    // Save tool results to history
    addToolResults(sessionID, toolResults);
  }

  // If we reach max rounds, return an error
  console.log("[Warning] Max tool rounds reached");
  return { msg: "Przepraszam, wystąpił problem z przetwarzaniem Twojego żądania." };
};

/**
 * POST /message - Main endpoint for operator messages
 */
app.post("/message", async (req, res) => {
  try {
    const { sessionID, msg } = req.body;

    // Validate request
    if (!sessionID || typeof sessionID !== "string") {
      return res.status(400).json({ error: "Missing or invalid sessionID" });
    }

    if (!msg || typeof msg !== "string") {
      return res.status(400).json({ error: "Missing or invalid msg" });
    }

    // Process message and get response
    const response = await processMessage(sessionID, msg);
    
    res.json(response);
  } catch (error) {
    console.error("[Error]", error);
    res.status(500).json({ 
      error: "Internal server error",
      msg: "Przepraszam, wystąpił błąd systemu. Spróbuj ponownie." 
    });
  }
});

/**
 * GET / - Health check
 */
app.get("/", (req, res) => {
  res.json({ 
    status: "ok", 
    message: "Logistics Assistant API is running" 
  });
});

// Start server
app.listen(PORT, () => {
  console.log(`\n🚀 Logistics Assistant API Server`);
  console.log(`📡 Listening on http://localhost:${PORT}`);
  console.log(`🎯 POST /message to interact with the assistant\n`);
});
