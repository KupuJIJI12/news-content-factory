import { useMe } from '../api/auth'

export const DEFAULT_TZ = 'Europe/Samara'
export const useCityTimeZone = () => useMe().data?.timeZone ?? DEFAULT_TZ
