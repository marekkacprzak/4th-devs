export type ErrorLike = { message?: string | null };

export type ChatResult = { text: string; reasoningTokens: number };

export type UsageInfo = { output_tokens_details?: { reasoning_tokens?: number } };

export type ResponseError = { message?: string } | null;

export type ResponseAPI = {
	usage?: UsageInfo;
	error?: ResponseError;
} & Record<string, unknown>;

export type Annotation = Record<string, unknown>;

export type OutputTextPart = {
	type: "output_text";
	text: string;
	annotations?: Annotation[];
	logprobs?: Array<number | null> | unknown[];
};

export type ResponseMessage = {
	id?: string;
	type?: string;
	status?: string;
	content?: Array<OutputTextPart | Record<string, unknown>>;
	role?: string;
};

export type ResponseObject = {
	id?: string;
	object?: string;
	created_at?: number;
	status?: string;
	background?: boolean;
	billing?: { payer?: string } & Record<string, unknown>;
	completed_at?: number | null;
	output_text?: string;
	frequency_penalty?: number;
	incomplete_details?: Record<string, unknown> | null;
	instructions?: string | null;
	max_output_tokens?: number | null;
	max_tool_calls?: number | null;
	model?: string;
	output?: ResponseMessage[] | null;
	parallel_tool_calls?: boolean;
	presence_penalty?: number;
	previous_response_id?: string | null;
	prompt_cache_key?: string | null;
	prompt_cache_retention?: number | null;
    usage?: UsageInfo | null;
	error?: ResponseError | null;
};

export type Message = { type: "message"; role: string; content: string | Array<OutputTextPart | Record<string, unknown>> };


