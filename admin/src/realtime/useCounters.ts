import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { countersKey, getCounters } from '../api/counters'
import type { Counters } from '../api/types'
import { pageTitle } from '../layout/CountersBar'

/** Live counters: initial GET, then SignalR "CountersChanged"; also keeps the tab title in sync. */
export function useCounters(): Counters | undefined {
  const queryClient = useQueryClient()
  const { data } = useQuery({ queryKey: countersKey, queryFn: getCounters, refetchInterval: 60_000 })

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/admin')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('CountersChanged', (counters: Counters) => {
      queryClient.setQueryData(countersKey, counters)
      void queryClient.invalidateQueries({ queryKey: ['stories'] })
    })
    connection.start().catch(() => { /* polling via refetchInterval keeps counters fresh */ })
    return () => { void connection.stop() }
  }, [queryClient])

  useEffect(() => {
    document.title = pageTitle(data?.onReview ?? 0)
  }, [data?.onReview])

  return data
}
