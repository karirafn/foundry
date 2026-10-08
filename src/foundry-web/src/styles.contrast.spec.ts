import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

function parseHex(css: string, token: string): string {
  const match = css.match(new RegExp(`${token}:\\s*(#[0-9a-fA-F]{6})`));
  if (!match) {
    throw new Error(`Token ${token} not found in stylesheet`);
  }
  return match[1];
}

function relativeLuminance(hex: string): number {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;

  const toLinear = (c: number): number =>
    c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);

  return 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);
}

function contrastRatio(hex1: string, hex2: string): number {
  const l1 = relativeLuminance(hex1);
  const l2 = relativeLuminance(hex2);
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

describe('WCAG 1.4.11 Non-text Contrast — --fd-border-control token', () => {
  let css: string;
  let borderControl: string;
  let bgCard: string;
  let bgCardHover: string;
  let bgPage: string;

  beforeAll(() => {
    // Arrange — parse token values from the real stylesheet (cwd is the Angular project root)
    css = readFileSync(resolve(process.cwd(), 'src/styles.scss'), 'utf-8');
    borderControl = parseHex(css, '--fd-border-control');
    bgCard = parseHex(css, '--fd-bg-card');
    bgCardHover = parseHex(css, '--fd-bg-card-hover');
    bgPage = parseHex(css, '--fd-bg-page');
  });

  it('--fd-border-control meets 3:1 contrast against --fd-bg-card', () => {
    // Act
    const ratio = contrastRatio(borderControl, bgCard);

    // Assert
    expect(ratio).toBeGreaterThanOrEqual(3);
  });

  it('--fd-border-control meets 3:1 contrast against --fd-bg-card-hover', () => {
    // Act
    const ratio = contrastRatio(borderControl, bgCardHover);

    // Assert
    expect(ratio).toBeGreaterThanOrEqual(3);
  });

  it('--fd-border-control meets 3:1 contrast against --fd-bg-page', () => {
    // Act
    const ratio = contrastRatio(borderControl, bgPage);

    // Assert
    expect(ratio).toBeGreaterThanOrEqual(3);
  });
});
