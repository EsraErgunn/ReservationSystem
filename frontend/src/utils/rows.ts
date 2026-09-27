const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'

/** 0 → A, 25 → Z, 26 → AA ... — mekân formunda sıra etiketi önerir. */
export function rowLabel(index: number): string {
  let label = ''
  let n = index
  do {
    label = LETTERS[n % 26] + label
    n = Math.floor(n / 26) - 1
  } while (n >= 0)
  return label
}
