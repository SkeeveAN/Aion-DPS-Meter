export const ARTICLE: {
  path: string;
  title: string;
  description: string;
  date: string;
  blocks: [string, string | string[] | { caption: string; steps: string[] }][];
};
export const ARTICLE_DE: typeof ARTICLE;
export function articleSegments(text: string): { text: string; bold?: boolean; href?: string }[];
export function flowStep(text: string): { text: string; kind: "step" | "gap" | "result" };
