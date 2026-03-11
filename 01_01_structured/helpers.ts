import type { OutputTextPart, ResponseObject } from "./types.ts";

export const extractResponseText = (data: ResponseObject): string => {
  if (typeof data?.output_text === "string" && data.output_text.trim()) {
    return data.output_text;
  }

  const messages = Array.isArray(data?.output)
    ? data.output.filter((item) => item?.type === "message")
    : [];

  const textPart = messages
    .flatMap((message) => (Array.isArray(message?.content) ? message.content : []))
    .find((part) => (part as OutputTextPart)?.type === "output_text" && typeof (part as OutputTextPart)?.text === "string") as OutputTextPart | undefined;

  return textPart?.text ?? "";
};
