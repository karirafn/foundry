import { TestBed } from '@angular/core/testing';
import { SecretFindingsComponent } from './secret-findings';
import { SecretFinding } from '../../workers/worker-run.model';

const findings: SecretFinding[] = [
  { path: 'src/Api/appsettings.json', line: 14, commitSha: 'abc1234def', ruleId: 'aws-access-key-id' },
  { path: 'scripts/deploy.sh', line: 3, commitSha: 'abc1234def', ruleId: 'generic-api-key' },
];

function render(items: readonly SecretFinding[], headingId = 'findings-heading-1') {
  TestBed.configureTestingModule({ imports: [SecretFindingsComponent] });
  const fixture = TestBed.createComponent(SecretFindingsComponent);
  fixture.componentRef.setInput('findings', items);
  fixture.componentRef.setInput('headingId', headingId);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const rows = Array.from(el.querySelectorAll('li'));
  const text = (node: Element): string => (node.textContent ?? '').replace(/\s+/g, ' ').trim();
  return { fixture, el, rows, text };
}

describe('SecretFindingsComponent', () => {
  it('should render a visible "Findings" heading with the given id', () => {
    // Arrange / Act
    const { el, text } = render(findings, 'my-heading');

    // Assert
    const heading = el.querySelector('#my-heading');
    expect(heading).not.toBeNull();
    expect(text(heading!)).toBe('Findings');
  });

  it('should label the list by the heading', () => {
    // Arrange / Act
    const { el } = render(findings, 'my-heading');

    // Assert
    const list = el.querySelector('ul');
    expect(list?.getAttribute('role')).toBe('list');
    expect(list?.getAttribute('aria-labelledby')).toBe('my-heading');
  });

  it('should render one row per finding with path, line and rule', () => {
    // Arrange / Act
    const { rows, text } = render(findings);

    // Assert
    expect(rows.length).toBe(2);
    const codes = Array.from(rows[0].querySelectorAll('code')).map((c) => text(c));
    expect(codes).toEqual(['src/Api/appsettings.json', 'aws-access-key-id']);
    expect(text(rows[0])).toContain('line 14');
    expect(text(rows[0])).toContain('Rule aws-access-key-id');
    expect(text(rows[1])).toContain('scripts/deploy.sh');
    expect(text(rows[1])).toContain('line 3');
    expect(text(rows[1])).toContain('Rule generic-api-key');
  });

  it('should render no links', () => {
    // Arrange / Act
    const { el } = render(findings);

    // Assert
    expect(el.querySelectorAll('a').length).toBe(0);
  });

  it('should render none of an unexpected extra property such as a matched value', () => {
    // Arrange
    const leaky = {
      path: 'config.yml',
      line: 7,
      commitSha: 'deadbeef',
      ruleId: 'generic-api-key',
      match: 'AKIA-SUPER-SECRET-VALUE',
      secret: 'ghp_leakedtokenvalue',
    } as SecretFinding;

    // Act
    const { el } = render([leaky]);

    // Assert
    const html = el.innerHTML;
    expect(html).not.toContain('AKIA-SUPER-SECRET-VALUE');
    expect(html).not.toContain('ghp_leakedtokenvalue');
  });

  it('should render markup in a path as text, not HTML', () => {
    // Arrange
    const hostile: SecretFinding = { path: '<img src=x onerror=alert(1)>.txt', line: 1, commitSha: 'a', ruleId: 'r' };

    // Act
    const { el, rows, text } = render([hostile]);

    // Assert
    expect(el.querySelector('img')).toBeNull();
    expect(text(rows[0])).toContain('<img src=x onerror=alert(1)>.txt');
  });
});
