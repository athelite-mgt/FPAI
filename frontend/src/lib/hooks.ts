import { useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, toQuery } from './api'
import type { Department, Lookup, PagedResult } from './types'

/** Debounces a value so typing in a search box does not fire a request per keystroke. */
export function useDebounced<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(timer)
  }, [value, delay])
  return debounced
}

export interface ListState {
  page: number
  search: string
  sortBy?: string
  sortDescending: boolean
  filters: Record<string, string>
  setPage: (page: number) => void
  setSearch: (search: string) => void
  setFilter: (key: string, value: string) => void
  toggleSort: (column: string) => void
  reset: () => void
}

/**
 * Keeps list state in the URL so a filtered view can be shared, bookmarked and
 * survives a refresh or a back-navigation from a detail page.
 */
export function useListState(defaultSort?: string): ListState {
  const [params, setParams] = useSearchParams()

  const page = Number(params.get('page') ?? '1') || 1
  const search = params.get('search') ?? ''
  const sortBy = params.get('sortBy') ?? defaultSort
  const sortDescending = params.get('desc') === '1'

  const filters = useMemo(() => {
    const out: Record<string, string> = {}
    params.forEach((value, key) => {
      if (!['page', 'search', 'sortBy', 'desc'].includes(key)) out[key] = value
    })
    return out
  }, [params])

  const update = useCallback(
    (mutate: (next: URLSearchParams) => void) => {
      setParams((current) => {
        const next = new URLSearchParams(current)
        mutate(next)
        return next
      }, { replace: true })
    },
    [setParams],
  )

  return {
    page,
    search,
    sortBy,
    sortDescending,
    filters,
    setPage: (value) => update((n) => n.set('page', String(value))),
    setSearch: (value) =>
      update((n) => {
        value ? n.set('search', value) : n.delete('search')
        n.set('page', '1') // a new search always starts at the first page
      }),
    setFilter: (key, value) =>
      update((n) => {
        value ? n.set(key, value) : n.delete(key)
        n.set('page', '1')
      }),
    toggleSort: (column) =>
      update((n) => {
        const currentSort = n.get('sortBy') ?? defaultSort
        if (currentSort === column) {
          n.set('desc', n.get('desc') === '1' ? '0' : '1')
        } else {
          n.set('sortBy', column)
          n.set('desc', '0')
        }
      }),
    reset: () => setParams(new URLSearchParams(), { replace: true }),
  }
}

/** Standard paged list query wired to useListState. */
export function usePagedQuery<T>(key: string, url: string, state: ListState, pageSize = 25) {
  const debouncedSearch = useDebounced(state.search)

  const query = useMemo(
    () =>
      toQuery({
        page: state.page,
        pageSize,
        search: debouncedSearch,
        sortBy: state.sortBy,
        sortDescending: state.sortDescending,
        ...state.filters,
      }),
    [state.page, pageSize, debouncedSearch, state.sortBy, state.sortDescending, state.filters],
  )

  return useQuery({
    queryKey: [key, query],
    queryFn: async () => (await api.get<PagedResult<T>>(url, { params: query })).data,
    placeholderData: (previous) => previous, // avoids a flash of empty table while paging
  })
}

export function useDepartments() {
  return useQuery({
    queryKey: ['departments'],
    queryFn: async () => (await api.get<Department[]>('/departments')).data,
    staleTime: 10 * 60_000,
  })
}

export function usePlayerLookup() {
  return useQuery({
    queryKey: ['players', 'lookup'],
    queryFn: async () => (await api.get<Lookup[]>('/players/lookup')).data,
    staleTime: 5 * 60_000,
  })
}

export function useUserLookup() {
  return useQuery({
    queryKey: ['users', 'lookup'],
    queryFn: async () => (await api.get<Lookup[]>('/users/lookup')).data,
    staleTime: 5 * 60_000,
  })
}

export function useVendors() {
  return useQuery({
    queryKey: ['vendors', 'all'],
    queryFn: async () =>
      (await api.get<PagedResult<{ id: string; name: string }>>('/vendors', {
        params: { pageSize: 200 },
      })).data.items,
    staleTime: 5 * 60_000,
  })
}

/** Turns an enum union into <option> entries with readable labels. */
export function enumOptions(values: readonly string[]): { value: string; label: string }[] {
  const special: Record<string, string> = {
    FifaDrc: 'FIFA DRC', Cas: 'CAS', Psc: 'PSC', GeneralBody: 'General Body',
  }
  return values.map((value) => ({
    value,
    label: special[value] ?? value.replace(/([a-z])([A-Z])/g, '$1 $2'),
  }))
}
