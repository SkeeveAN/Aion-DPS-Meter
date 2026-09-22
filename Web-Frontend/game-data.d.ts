export interface LocalizedName {
  en?: string;
  de?: string;
  fr?: string;
  es?: string;
  ru?: string;
  pl?: string;
  tr?: string;
  zh?: string;
}

export const GAME_NAME_TRANSLATIONS: Record<string, LocalizedName>;
export const INSTANCE_IMAGES: Record<string, string>;
export const BOSS_IMAGES: Record<string, string>;
export const INSTANCE_MIN_LEVEL: Record<string, number>;
