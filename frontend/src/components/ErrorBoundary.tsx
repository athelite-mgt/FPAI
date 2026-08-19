import { Component, type ReactNode } from 'react'
import { Button, EmptyState } from './ui'

interface Props { children: ReactNode }
interface State { error: Error | null }

/**
 * Without this, an uncaught render error anywhere in the tree unmounts the whole app — one
 * bad field on one record blanks every page, not just the one that threw it. Confining that
 * here means a broken record degrades to an error card instead of taking the session down.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: { componentStack?: string | null }) {
    console.error('Unhandled render error:', error, info.componentStack)
  }

  render() {
    if (this.state.error) {
      return (
        <div className="flex h-full items-center justify-center p-6">
          <EmptyState
            title="Something went wrong"
            description="This page hit an unexpected error. Reloading usually fixes it."
            action={<Button onClick={() => window.location.reload()}>Reload</Button>}
          />
        </div>
      )
    }
    return this.props.children
  }
}
