import { apiFetch } from './client'
import type { ChannelInput, ChannelSettings, City, CityInput } from './types'

export const citiesKey = ['cities'] as const
export const listCities = () => apiFetch<City[]>('/api/cities')
export const createCity = (input: CityInput) => apiFetch<City>('/api/cities', { method: 'POST', json: input })
export const updateCity = (id: number, input: CityInput) => apiFetch<City>(`/api/cities/${id}`, { method: 'PUT', json: input })
export const putCityChannel = (cityId: number, input: ChannelInput) =>
  apiFetch<ChannelSettings>(`/api/cities/${cityId}/channel`, { method: 'PUT', json: input })
