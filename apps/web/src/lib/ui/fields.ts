import { can, type Session } from "../security/permissions.ts";
import { validateDate, validateNumeric, type ValidationResult } from "./validation.ts";

// Dinamik alan tanımı — .NET FieldDefinition/FieldVisibility ile eşit. Sabit yazılmaz.
export type FieldType = "text" | "numeric" | "date" | "lookup" | "multiselect" | "photo";

export interface FieldDefinition {
  key: string;
  label: string;
  type: FieldType;
  moduleKey: string;
  isLookup?: boolean;
  allowMultiSelect?: boolean;
  hasPhoto?: boolean;
  allowAdd?: boolean;
  required?: boolean;
  min?: number;
  max?: number;
  allowNegative?: boolean;
}

export const isFieldVisible = (s: Session, f: FieldDefinition): boolean => can(s, f.moduleKey, "view");

export const isFieldEditable = (s: Session, f: FieldDefinition): boolean =>
  can(s, f.moduleKey, "create") || can(s, f.moduleKey, "edit");

// "+" yeni lookup ekleme: allowAdd + yazma yetkisi (deny-by-default).
export const canShowAddButton = (s: Session, f: FieldDefinition): boolean =>
  !!f.allowAdd && can(s, f.moduleKey, "create");

export function validateFieldValue(
  f: FieldDefinition,
  raw: string | null,
  numeric: number | null,
): ValidationResult {
  if (f.required && !raw && (numeric === null || numeric === undefined)) {
    return { ok: false, error: `${f.label} zorunlu.` };
  }
  if (f.type === "date" && raw) return validateDate(raw);
  if (f.type === "numeric" && (numeric !== null || f.required)) {
    return validateNumeric(numeric, { min: f.min, max: f.max, allowNegative: f.allowNegative });
  }
  return { ok: true };
}
