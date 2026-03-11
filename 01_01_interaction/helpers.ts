import type { OutputTextPart, ResponseObject, Message } from "./types.ts";

export const extractResponseText = (data: ResponseObject): string => {
  //logStructure("extractResponseText.data", data);
  if (typeof data?.output_text === "string" && data.output_text.trim()) {
    return data.output_text;
  }
  
  const messages = Array.isArray(data?.output)
    ? data.output.filter((item) => item?.type === "message")
    : [];

    /*
  // log each message.content shape so we can refine types
  for (const [i, msg] of messages.entries()) {
    logStructure(`message[${i}].content`, msg.content);
    if (Array.isArray(msg.content)) {
      for (const [j, part] of msg.content.entries()) {
        logStructure(`message[${i}].content[${j}]`, part);
      }
    }
  }
*/

  const textPart = messages
    .flatMap((message) => (Array.isArray(message?.content) ? message.content : []))
    .find((part) => (part as OutputTextPart | Record<string, unknown>)?.type === "output_text" && typeof (part as OutputTextPart)?.text === "string") as OutputTextPart | undefined;

  return textPart?.text ?? "";
};

export const toMessage = (role: string, content: string | Array<OutputTextPart | Record<string, unknown>>): Message => {
  //logStructure("toMessage.content", content);
  return { type: "message", role, content };
};

export function logStructure(name: string, obj: unknown) {
      console.log(`[STRUCTURE] ${name}:`, JSON.stringify(obj, null, 2));
}
