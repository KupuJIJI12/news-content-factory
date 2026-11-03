import { createTheme, type MantineColorsTuple } from '@mantine/core'
import { tokens } from './design/tokens'

const ink: MantineColorsTuple = ['#F2F4F7', '#E4E8ED', '#C8D0D9', '#AAB5C2', '#8E9AAA', '#717F91', '#5A6675', '#3A4552', '#1C2633', '#121922']
const amber: MantineColorsTuple = ['#FDF6E8', '#FBEFD8', '#F5DCAE', '#EFC780', '#E8B257', '#E1A03A', '#D9921A', '#B87A12', '#93610C', '#6B4A0E']
const teal: MantineColorsTuple = ['#E6F4F1', '#C5E6DF', '#9AD3C7', '#6CBFAD', '#43AB95', '#26987F', '#16806F', '#11685A', '#0C5146', '#073A32']
const brick: MantineColorsTuple = ['#FBEDEA', '#F4D2CB', '#EAAEA2', '#DE8877', '#D26752', '#C5503A', '#B8432F', '#983626', '#782A1E', '#581F16']

export const theme = createTheme({
  primaryColor: 'ink',
  primaryShade: 8,
  colors: { ink, amber, teal, brick },
  black: tokens.ink,
  fontFamily: tokens.text,
  headings: { fontFamily: tokens.display, fontWeight: '600' },
  radius: { xs: '2px', sm: '6px', md: '8px', lg: '12px', xl: '16px' },
  defaultRadius: 'sm',
  cursorType: 'pointer',
  components: {
    Badge: { defaultProps: { radius: 'sm' }, styles: { root: { textTransform: 'none', fontWeight: 500, letterSpacing: 0 } } },
    Paper: { defaultProps: { radius: 'md' } },
    Card: { defaultProps: { radius: 'md' } },
    Alert: { defaultProps: { radius: 'md', variant: 'light' } },
    Modal: { defaultProps: { radius: 'md' } },
    Drawer: { styles: { title: { fontFamily: tokens.display, fontWeight: 600 } } },
    Tabs: { styles: { tab: { fontWeight: 500 } } },
  },
})
