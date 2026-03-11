/**
 * Session management - maintains conversation history per sessionID
 */

// In-memory store: sessionID -> conversation array
const sessions = new Map();

/**
 * Gets conversation history for a session
 * @param {string} sessionID 
 * @returns {Array} conversation messages
 */
export const getSession = (sessionID) => {
  if (!sessions.has(sessionID)) {
    sessions.set(sessionID, []);
  }
  return sessions.get(sessionID);
};

/**
 * Adds a user message to session history
 * @param {string} sessionID 
 * @param {string} message 
 */
export const addUserMessage = (sessionID, message) => {
  const conversation = getSession(sessionID);
  conversation.push({ role: "user", content: message });
};

/**
 * Adds assistant response to session history
 * @param {string} sessionID 
 * @param {Array} output - API output array (messages, tool calls, etc.)
 */
export const addAssistantOutput = (sessionID, output) => {
  const conversation = getSession(sessionID);
  conversation.push(...output);
};

/**
 * Adds tool results to session history
 * @param {string} sessionID 
 * @param {Array} toolResults 
 */
export const addToolResults = (sessionID, toolResults) => {
  const conversation = getSession(sessionID);
  conversation.push(...toolResults);
};

/**
 * Clears session history (optional, for testing)
 * @param {string} sessionID 
 */
export const clearSession = (sessionID) => {
  sessions.delete(sessionID);
};

/**
 * Gets all active session IDs (for debugging)
 * @returns {Array<string>}
 */
export const getActiveSessions = () => {
  return Array.from(sessions.keys());
};
