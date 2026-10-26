import { Stack, Tabs, Title } from '@mantine/core'
import { ChannelSettings } from './ChannelSettings'
import { CitySettings } from './CitySettings'
import { LlmSettings } from './LlmSettings'
import { PromptSettings } from './PromptSettings'
import { UsageSettings } from './UsageSettings'

export function SettingsPage() {
  return (
    <Stack gap="sm">
      <Title order={3}>Настройки</Title>
      <Tabs defaultValue="city" keepMounted={false}>
        <Tabs.List style={{ overflowX: 'auto', flexWrap: 'nowrap' }}>
          <Tabs.Tab value="city">Город</Tabs.Tab>
          <Tabs.Tab value="channel">Канал</Tabs.Tab>
          <Tabs.Tab value="llm">LLM</Tabs.Tab>
          <Tabs.Tab value="prompts">Промпты</Tabs.Tab>
          <Tabs.Tab value="usage">Расход</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="city" pt="md"><CitySettings /></Tabs.Panel>
        <Tabs.Panel value="channel" pt="md"><ChannelSettings /></Tabs.Panel>
        <Tabs.Panel value="llm" pt="md"><LlmSettings /></Tabs.Panel>
        <Tabs.Panel value="prompts" pt="md"><PromptSettings /></Tabs.Panel>
        <Tabs.Panel value="usage" pt="md"><UsageSettings /></Tabs.Panel>
      </Tabs>
    </Stack>
  )
}
