import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Badge, Button, Input, Pagination, priorityTone, Select, statusTone, WorkflowRail } from './ui'

describe('statusTone', () => {
  it('maps terminal-good states to success', () => {
    for (const status of ['Resolved', 'Approved', 'Reconciled', 'Passed', 'Done', 'Completed']) {
      expect(statusTone(status), status).toBe('success')
    }
  })

  it('maps failure states to danger', () => {
    for (const status of ['Rejected', 'Failed', 'Cancelled', 'Blocked', 'Suspended']) {
      expect(statusTone(status), status).toBe('danger')
    }
  })

  it('maps in-flight states to info or warning', () => {
    expect(statusTone('UnderReview')).toBe('info')
    expect(statusTone('InProgress')).toBe('warning')
  })

  it('falls back to neutral for an unknown status', () => {
    expect(statusTone('SomethingNew')).toBe('neutral')
  })
})

describe('priorityTone', () => {
  it('escalates with priority', () => {
    expect(priorityTone('Low')).toBe('neutral')
    expect(priorityTone('Medium')).toBe('info')
    expect(priorityTone('High')).toBe('warning')
    expect(priorityTone('Critical')).toBe('danger')
  })
})

describe('Button', () => {
  it('calls its handler when clicked', async () => {
    const onClick = vi.fn()
    render(<Button onClick={onClick}>Save</Button>)
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('is disabled and unclickable while loading', async () => {
    const onClick = vi.fn()
    render(<Button loading onClick={onClick}>Save</Button>)

    const button = screen.getByRole('button')
    expect(button).toBeDisabled()
    await userEvent.click(button, { pointerEventsCheck: 0 })
    expect(onClick).not.toHaveBeenCalled()
  })
})

describe('Input', () => {
  it('associates its label with the control', () => {
    render(<Input label="Case title" />)
    expect(screen.getByLabelText(/Case title/)).toBeInTheDocument()
  })

  it('shows an error and marks the field invalid', () => {
    render(<Input label="Amount" error="Enter an amount greater than zero." />)
    expect(screen.getByText('Enter an amount greater than zero.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Amount/)).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows a hint when there is no error', () => {
    render(<Input label="Password" hint="At least 10 characters." />)
    expect(screen.getByText('At least 10 characters.')).toBeInTheDocument()
  })

  it('prefers the error over the hint', () => {
    render(<Input label="Password" hint="At least 10 characters." error="Too short." />)
    expect(screen.getByText('Too short.')).toBeInTheDocument()
    expect(screen.queryByText('At least 10 characters.')).not.toBeInTheDocument()
  })
})

describe('Select', () => {
  it('renders a placeholder plus its options', () => {
    render(
      <Select label="Status" placeholder="All statuses"
        options={[{ value: 'New', label: 'New' }, { value: 'Closed', label: 'Closed' }]} />,
    )
    expect(screen.getByRole('option', { name: 'All statuses' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'New' })).toBeInTheDocument()
  })
})

describe('Pagination', () => {
  it('describes the current window', () => {
    render(<Pagination page={2} pageSize={25} totalCount={130} totalPages={6} onPage={() => {}} />)
    expect(screen.getByText('26–50')).toBeInTheDocument()
    expect(screen.getByText('130')).toBeInTheDocument()
  })

  it('disables Prev on the first page and Next on the last', () => {
    const { rerender } = render(
      <Pagination page={1} pageSize={25} totalCount={130} totalPages={6} onPage={() => {}} />,
    )
    expect(screen.getByRole('button', { name: /Prev/ })).toBeDisabled()

    rerender(<Pagination page={6} pageSize={25} totalCount={130} totalPages={6} onPage={() => {}} />)
    expect(screen.getByRole('button', { name: /Next/ })).toBeDisabled()
  })

  it('renders nothing when there is no data', () => {
    const { container } = render(
      <Pagination page={1} pageSize={25} totalCount={0} totalPages={0} onPage={() => {}} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('asks for the next page when Next is pressed', async () => {
    const onPage = vi.fn()
    render(<Pagination page={2} pageSize={25} totalCount={130} totalPages={6} onPage={onPage} />)
    await userEvent.click(screen.getByRole('button', { name: /Next/ }))
    expect(onPage).toHaveBeenCalledWith(3)
  })
})

describe('WorkflowRail', () => {
  it('renders each step with the current one marked', () => {
    render(<WorkflowRail steps={['New', 'UnderReview', 'Closed']} current="UnderReview" />)
    expect(screen.getByText('New')).toBeInTheDocument()
    expect(screen.getByText('Under Review')).toBeInTheDocument()
    expect(screen.getByText('Closed')).toBeInTheDocument()
  })
})

describe('Badge', () => {
  it('renders its content', () => {
    render(<Badge tone="success">Approved</Badge>)
    expect(screen.getByText('Approved')).toBeInTheDocument()
  })
})
