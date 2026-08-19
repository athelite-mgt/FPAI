/** The FPAI crest. Transparent PNG, works on both the dark chrome and light surfaces. */
export function FpaiMark({ className = 'size-8' }: { className?: string }) {
  return (
    <img
      src="/brand/fpai-logo.png"
      alt="FPAI"
      draggable={false}
      className={`${className} object-contain select-none`}
    />
  )
}

/**
 * Attribution block for the app's build partner. The wordmark is only shipped in a
 * near-white repaint (`athelite-logo-dark.png`) because every placement in this app sits
 * on the dark `--chrome` surface, which stays dark across all six colour schemes.
 */
export function PoweredByAthelite({ compact = false, className = '' }: { compact?: boolean; className?: string }) {
  return (
    <div className={`flex items-center ${compact ? 'gap-1.5' : 'flex-col gap-1.5'} ${className}`}>
      <span className="text-[9px] font-medium tracking-[0.15em] text-[var(--chrome-muted)] uppercase">
        Powered by
      </span>
      <img
        src="/brand/athelite-logo-dark.png"
        alt="Athelite"
        draggable={false}
        className={`${compact ? 'h-4' : 'h-6'} w-auto object-contain opacity-80 select-none`}
      />
    </div>
  )
}
