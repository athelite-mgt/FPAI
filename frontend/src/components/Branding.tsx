/**
 * The animated identity moment for the login page's dark brand panel: a stylised pitch
 * (halfway line, two end boxes, a breathing centre circle) behind the crest, with a
 * slowly turning dashed ring and a few drifting marker dots. Adapted from the
 * FPAI Login Animation design — kept to a CSS-only loop here rather than the original's
 * continuously re-rendering canvas, both to avoid its unresolved text-jitter bug and
 * because a login page shouldn't burn CPU on a decorative background. Nothing here ever
 * animates the text itself, only the decorative shapes and the crest's own float/scale.
 */
export function LoginHero() {
  return (
    <div className="relative flex flex-1 items-center justify-center">
      <div className="pointer-events-none absolute inset-0 overflow-hidden" aria-hidden>
        <div className="absolute inset-x-[14%] top-[8%] h-[20%] rounded-sm border border-white/10" />
        <div className="absolute inset-x-[14%] bottom-[8%] h-[20%] rounded-sm border border-white/10" />
        <div className="absolute inset-x-0 top-1/2 h-px -translate-y-1/2 bg-white/10" />

        <div className="absolute top-1/2 left-1/2 size-72 -translate-x-1/2 -translate-y-1/2">
          <div className="fpai-hero-halo absolute inset-8 rounded-full border border-white/15" />
          <div className="fpai-hero-ring absolute inset-4 rounded-full border border-dashed border-white/10" />

          <div className="fpai-hero-ring absolute inset-0 [animation-duration:22s]">
            <span className="fpai-hero-dot absolute top-0 left-1/2 size-1.5 -translate-x-1/2 rounded-full bg-white/80" />
          </div>
          <div className="fpai-hero-ring-reverse absolute inset-2 [animation-duration:30s]">
            <span
              className="fpai-hero-dot absolute top-1/2 right-0 size-1.5 -translate-y-1/2 rounded-full bg-[#fb923c]"
              style={{ animationDelay: '1.5s' }}
            />
          </div>
          <div className="fpai-hero-ring absolute inset-6 [animation-duration:26s] [animation-direction:reverse]">
            <span
              className="fpai-hero-dot absolute bottom-1 left-1/3 size-1 rounded-full bg-[#2dd4bf]"
              style={{ animationDelay: '3s' }}
            />
          </div>
        </div>
      </div>

      <div className="relative flex flex-col items-center text-center">
        <FpaiMark className="fpai-hero-crest size-24" />
        <p className="mt-6 text-sm font-medium text-white">Football Players Association of India</p>
        <div className="mt-3 h-px w-10 bg-white/20" />
        <p className="mt-3 text-[11px] font-medium tracking-[0.25em] text-[var(--chrome-muted)] uppercase">
          Powered by Athelite
        </p>
      </div>
    </div>
  )
}

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
