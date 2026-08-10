import { describe, it, expect } from 'vitest';
import { screen } from '@testing-library/react';
import { MenuItem } from '@mui/material';
import { renderWithProviders } from '@/test/renderWithProviders';
import { AppTextField } from '@/components/atoms';
import { FormField } from './FormField';

/**
 * Elements a `<label for>` is allowed to point at. Chrome reports "Incorrect use of
 * `<label for=FORM_ELEMENT>`" for anything else — which is what a MUI `<TextField select>` produces,
 * since it renders the id on a `<div role="combobox">`.
 */
const LABELABLE_TAGS: string[] = [
  'BUTTON',
  'INPUT',
  'METER',
  'OUTPUT',
  'PROGRESS',
  'SELECT',
  'TEXTAREA'
];

/** Every `label[for]` in the document paired with the tag name of the element it points at. */
function labelTargets(): { label: string; targetTag: string | null }[] {
  return Array.from(document.querySelectorAll('label[for]')).map((label) => {
    const target = document.getElementById(label.getAttribute('for') as string);
    return { label: label.textContent ?? '', targetTag: target?.tagName ?? null };
  });
}

function SelectField() {
  return (
    <FormField label="Method" required>
      <AppTextField select value="Cash" onChange={() => undefined}>
        <MenuItem value="Cash">Cash</MenuItem>
        <MenuItem value="BankTransfer">Bank transfer</MenuItem>
      </AppTextField>
    </FormField>
  );
}

function TextFieldRow() {
  return (
    <FormField label="Bank reference">
      <AppTextField value="ABC" onChange={() => undefined} />
    </FormField>
  );
}

describe('FormField label association (ui-validate D7 — a11y)', () => {
  it('FormField_Select_UsesAriaLabelledByInsteadOfLabelFor', async () => {
    renderWithProviders(<SelectField />);

    const combobox = await screen.findByRole('combobox');
    const label = document.querySelector('label') as HTMLLabelElement;

    // The label no longer claims to label a DIV…
    expect(label.getAttribute('for')).toBeNull();
    // …it is referenced BY the combobox instead, which is the valid direction for a non-labelable role.
    expect(combobox.getAttribute('aria-labelledby')).toContain(label.id);
    expect(label.id.length).toBeGreaterThan(0);
  });

  it('FormField_Select_StillResolvesItsAccessibleName', async () => {
    renderWithProviders(<SelectField />);

    // The whole point of the association: the control is still reachable by its visible label.
    const byLabel = await screen.findByLabelText(/Method/);
    expect(byLabel).toHaveAttribute('role', 'combobox');
  });

  it('FormField_TextInput_KeepsTheLabelForAssociationOnALabelableElement', async () => {
    renderWithProviders(<TextFieldRow />);

    const input = await screen.findByRole('textbox');
    const label = document.querySelector('label') as HTMLLabelElement;

    expect(label.getAttribute('for')).toBe(input.id);
    expect(input.tagName).toBe('INPUT');
    expect(screen.getByLabelText('Bank reference')).toBe(input);
  });

  it('FormField_MixedForm_NoLabelForPointsAtANonLabelableElement', async () => {
    renderWithProviders(
      <>
        <SelectField />
        <TextFieldRow />
        <FormField label="Payment date">
          <AppTextField type="date" value="2026-08-05" onChange={() => undefined} />
        </FormField>
      </>
    );

    await screen.findByRole('combobox');

    const targets = labelTargets();
    expect(targets.length).toBeGreaterThan(0);
    for (const { label, targetTag } of targets) {
      expect(LABELABLE_TAGS, `label "${label}" points at <${targetTag}>`).toContain(targetTag);
    }
  });

  it('FormField_ExplicitHtmlForAndChildOwnedId_AreStillHonoured', async () => {
    renderWithProviders(
      <>
        <FormField label="Amount" htmlFor="amount-input">
          <AppTextField id="amount-input" value="1" onChange={() => undefined} />
        </FormField>
        <FormField label="Rate">
          <AppTextField id="rate-input" value="1" onChange={() => undefined} />
        </FormField>
      </>
    );

    expect(await screen.findByLabelText('Amount')).toHaveAttribute('id', 'amount-input');
    expect(screen.getByLabelText('Rate')).toHaveAttribute('id', 'rate-input');
  });
});
