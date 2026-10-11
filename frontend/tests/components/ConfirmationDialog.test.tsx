import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ConfirmationDialog } from '../../src/components/ConfirmationDialog'

function renderDialog(props: Record<string, unknown> = {}) {
  const onKeep = vi.fn()
  const onConfirm = vi.fn()
  render(
    <ConfirmationDialog
      title="Delete this order?"
      keepLabel="Keep order"
      confirmLabel="Delete order"
      onKeep={onKeep}
      onConfirm={onConfirm}
      {...props}
    >
      <p>This cannot be undone.</p>
    </ConfirmationDialog>,
  )
  return { onKeep, onConfirm }
}

describe('ConfirmationDialog', () => {
  it('renders the title, body and labels as given', () => {
    renderDialog()

    expect(screen.getByRole('alertdialog', { name: 'Delete this order?' })).toBeInTheDocument()
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Keep order' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete order' })).toBeInTheDocument()
  })

  it('starts with focus on the keep button, not the confirm button', () => {
    renderDialog()

    expect(screen.getByRole('button', { name: 'Keep order' })).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Delete order' })).not.toHaveFocus()
  })

  it('styles the confirm button as destructive when destructive', () => {
    renderDialog({ destructive: true })

    expect(screen.getByRole('button', { name: 'Delete order' })).toHaveClass(
      'confirmation-dialog__confirm--destructive',
    )
    expect(screen.getByRole('button', { name: 'Keep order' })).not.toHaveClass(
      'confirmation-dialog__confirm--destructive',
    )
  })

  it('does not style the confirm button as destructive by default', () => {
    renderDialog()

    expect(screen.getByRole('button', { name: 'Delete order' })).not.toHaveClass(
      'confirmation-dialog__confirm--destructive',
    )
  })

  it('keeps confirm disabled until exactly the confirm word is typed', async () => {
    const user = userEvent.setup()
    const { onConfirm } = renderDialog({ confirmWord: 'DELETE' })
    const confirm = screen.getByRole('button', { name: 'Delete order' })
    const input = screen.getByLabelText(/DELETE/)

    expect(confirm).toBeDisabled()

    await user.type(input, 'delete')
    expect(confirm).toBeDisabled()

    await user.clear(input)
    await user.type(input, 'DELETE ')
    expect(confirm).toBeDisabled()

    await user.clear(input)
    await user.type(input, 'DELETE')
    expect(confirm).toBeEnabled()

    await user.click(confirm)
    expect(onConfirm).toHaveBeenCalledTimes(1)
  })

  it('disables both buttons while busy', () => {
    renderDialog({ busy: true })

    expect(screen.getByRole('button', { name: 'Keep order' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Delete order' })).toBeDisabled()
  })

  it('calls onKeep once when keep is pressed', async () => {
    const user = userEvent.setup()
    const { onKeep, onConfirm } = renderDialog()

    await user.click(screen.getByRole('button', { name: 'Keep order' }))

    expect(onKeep).toHaveBeenCalledTimes(1)
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('calls onConfirm once when confirm is pressed', async () => {
    const user = userEvent.setup()
    const { onKeep, onConfirm } = renderDialog()

    await user.click(screen.getByRole('button', { name: 'Delete order' }))

    expect(onConfirm).toHaveBeenCalledTimes(1)
    expect(onKeep).not.toHaveBeenCalled()
  })
})
