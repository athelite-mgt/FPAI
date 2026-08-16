import { describe, expect, it } from 'vitest'
import {
  formatBytes, formatCompactCurrency, formatCurrency, formatDate, formatNumber,
  humanise, initialsOf, toDateInputValue,
} from './format'

describe('formatCompactCurrency', () => {
  it('uses the Indian short scale', () => {
    expect(formatCompactCurrency(1_480_000)).toBe('₹14.8L')
    expect(formatCompactCurrency(25_000_000)).toBe('₹2.5Cr')
    expect(formatCompactCurrency(45_000)).toBe('₹45K')
  })

  it('drops a trailing .0', () => {
    expect(formatCompactCurrency(200_000)).toBe('₹2L')
    expect(formatCompactCurrency(10_000_000)).toBe('₹1Cr')
  })

  it('handles small, zero and negative amounts', () => {
    expect(formatCompactCurrency(0)).toContain('0')
    expect(formatCompactCurrency(-500_000)).toBe('₹-5L')
  })

  it('renders nothing for a missing value rather than NaN', () => {
    expect(formatCompactCurrency(null)).toBe('—')
    expect(formatCompactCurrency(undefined)).toBe('—')
  })
})

describe('formatCurrency', () => {
  it('formats with the rupee symbol', () => {
    expect(formatCurrency(59000)).toContain('59,000')
  })
  it('returns a dash for a missing value', () => {
    expect(formatCurrency(null)).toBe('—')
  })
})

describe('formatNumber', () => {
  it('groups in the Indian style', () => {
    expect(formatNumber(1234567)).toBe('12,34,567')
  })
})

describe('formatDate', () => {
  it('formats an ISO timestamp', () => {
    expect(formatDate('2026-03-15T10:30:00Z')).toMatch(/15 Mar 2026/)
  })
  it('rejects an unparseable value instead of showing "Invalid Date"', () => {
    expect(formatDate('not-a-date')).toBe('—')
    expect(formatDate(null)).toBe('—')
  })
})

describe('toDateInputValue', () => {
  it('produces a yyyy-MM-dd value for date inputs', () => {
    expect(toDateInputValue('2026-03-05T00:00:00Z')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
  it('returns an empty string for a missing value', () => {
    expect(toDateInputValue(undefined)).toBe('')
  })
})

describe('humanise', () => {
  it('expands the domain abbreviations', () => {
    expect(humanise('FifaDrc')).toBe('FIFA DRC')
    expect(humanise('Cas')).toBe('CAS')
    expect(humanise('Psc')).toBe('PSC')
  })

  it('splits camel case into words', () => {
    expect(humanise('UnderReview')).toBe('Under Review')
    expect(humanise('MentalHealth')).toBe('Mental Health')
    expect(humanise('AccountantReview')).toBe('Accountant Review')
  })

  it('expands role names', () => {
    expect(humanise('SuperAdmin')).toBe('Super Admin')
    expect(humanise('ExternalAccountant')).toBe('External Accountant')
  })

  it('handles empty input', () => {
    expect(humanise('')).toBe('—')
    expect(humanise(null)).toBe('—')
  })
})

describe('formatBytes', () => {
  it('scales the unit', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(2048)).toBe('2 KB')
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB')
  })
})

describe('initialsOf', () => {
  it('takes at most two initials', () => {
    expect(initialsOf('Arjun Nair')).toBe('AN')
    expect(initialsOf('Priya')).toBe('P')
    expect(initialsOf('Lallianzuala Chhangte Junior')).toBe('LC')
  })
})
