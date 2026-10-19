export interface GuideColumn {
  name: string;
  url?: string;
  ours?: boolean;
  cells: Record<string, string>;
}
export const GUIDE_ASOF: string;
export const GUIDE_ROWS: string[];
export const GUIDE_TABLES: { id: string; columns: GuideColumn[] }[];
export const GUIDE_WHAT: string[];
export const GUIDE_WEAK: string[];
export const GUIDE_STRONG: string[];
export const GUIDE_OSS: string[];
export const GUIDE_FAQ: string[];
