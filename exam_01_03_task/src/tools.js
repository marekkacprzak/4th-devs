/**
 * Package API tools - integration with external package management system
 */

import "dotenv/config";

const PACKAGE_API_URL = "https://hub.ag3nts.org/api/packages";
const API_KEY = process.env.USER_API_KEY;

if (!API_KEY) {
  throw new Error("USER_API_KEY not found in environment variables");
}

/**
 * Check package status and location
 * @param {string} packageid 
 * @returns {Promise<object>}
 */
const checkPackage = async (packageid) => {
  const response = await fetch(PACKAGE_API_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      apikey: API_KEY,
      action: "check",
      packageid
    })
  });

  const data = await response.json();
  
  if (!response.ok) {
    throw new Error(`Package check failed: ${data.error || response.statusText}`);
  }

  return data;
};

/**
 * Redirect package to new destination
 * @param {string} packageid 
 * @param {string} destination 
 * @param {string} code - Security code
 * @returns {Promise<object>}
 */
const redirectPackage = async (packageid, destination, code) => {
  const response = await fetch(PACKAGE_API_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      apikey: API_KEY,
      action: "redirect",
      packageid,
      destination,
      code
    })
  });

  const data = await response.json();
  
  if (!response.ok) {
    throw new Error(`Package redirect failed: ${data.error || response.statusText}`);
  }

  return data;
};

// OpenAI function definitions
export const tools = [
  {
    type: "function",
    name: "check_package",
    description: "Sprawdza status i lokalizację paczki w systemie logistycznym",
    parameters: {
      type: "object",
      properties: {
        packageid: {
          type: "string",
          description: "Identyfikator paczki (np. PKG12345678)"
        }
      },
      required: ["packageid"],
      additionalProperties: false
    },
    strict: true
  },
  {
    type: "function",
    name: "redirect_package",
    description: "Przekierowuje paczkę do nowej lokalizacji docelowej",
    parameters: {
      type: "object",
      properties: {
        packageid: {
          type: "string",
          description: "Identyfikator paczki (np. PKG12345678)"
        },
        destination: {
          type: "string",
          description: "Kod lokalizacji docelowej (np. PWR3847PL)"
        },
        code: {
          type: "string",
          description: "Kod zabezpieczający wymagany do przekierowania"
        }
      },
      required: ["packageid", "destination", "code"],
      additionalProperties: false
    },
    strict: true
  }
];

// Tool handlers
export const handlers = {
  check_package: async (args) => {
    return await checkPackage(args.packageid);
  },
  redirect_package: async (args) => {
    return await redirectPackage(args.packageid, args.destination, args.code);
  }
};
