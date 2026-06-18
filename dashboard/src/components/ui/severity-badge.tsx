import { AlertCircle, AlertTriangle, Info, Bell, type LucideIcon } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Badge } from './badge';

/**
 * SeverityBadge — Molecule #24 (Atomic Design inventory, Design Phase Day 7-8 §1).
 *
 * Maps the 5-level severity hierarchy (Definition Phase decision #4,
 * mirrors Microsoft Defender XDR taxonomy) to Badge variants + icons.
 * Icon + color + translated label together satisfy WCAG 1.4.1 (Use of
 * Color) per Design Phase Day 6 §5.
 */

export type Severity = 'critical' | 'high' | 'medium' | 'low' | 'info';

const SEVERITY_ICON: Record<Severity, LucideIcon> = {
  critical: AlertCircle,
  high: AlertTriangle,
  medium: AlertTriangle,
  low: Info,
  info: Bell,
};

interface SeverityBadgeProps {
  severity: Severity;
  className?: string;
}

export function SeverityBadge({ severity, className }: SeverityBadgeProps) {
  const { t } = useTranslation();
  const Icon = SEVERITY_ICON[severity];

  return (
    <Badge variant={severity} className={className}>
      <Icon className="size-3" aria-hidden="true" />
      {t(`severity.${severity}`)}
    </Badge>
  );
}
