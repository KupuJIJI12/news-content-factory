import { useQuery } from '@tanstack/react-query'
import { citiesKey, listCities } from '../api/cities'

/** All cities; the city filter and city badges are shown only when there is more than one. */
export function useCities() {
  const { data } = useQuery({ queryKey: citiesKey, queryFn: listCities, staleTime: 60_000 })
  const cities = data ?? []
  return { cities, multi: cities.length > 1, loaded: data !== undefined }
}
