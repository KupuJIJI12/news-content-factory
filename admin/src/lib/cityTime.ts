import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc'
import timezone from 'dayjs/plugin/timezone'
import customParseFormat from 'dayjs/plugin/customParseFormat'

dayjs.extend(utc)
dayjs.extend(timezone)
dayjs.extend(customParseFormat)

export const WALL_FORMAT = 'YYYY-MM-DD HH:mm'

/** Wall time typed by the moderator ('YYYY-MM-DD HH:mm') interpreted in the city zone → ISO with the city offset. */
export function toCityIso(wallTime: string, tz: string): string {
  return dayjs.tz(wallTime, WALL_FORMAT, tz).format('YYYY-MM-DDTHH:mm:ssZ')
}

/** An instant (ISO, usually UTC) shown as wall time of the city. */
export function formatInCity(iso: string, tz: string, pattern = 'DD.MM HH:mm'): string {
  return dayjs(iso).tz(tz).format(pattern)
}

export function isPastInCity(wallTime: string, tz: string, now: number = Date.now()): boolean {
  return dayjs.tz(wallTime, WALL_FORMAT, tz).valueOf() <= now
}

/** Current wall time of the city, formatted. */
export function nowInCity(tz: string, pattern = WALL_FORMAT): string {
  return dayjs().tz(tz).format(pattern)
}
