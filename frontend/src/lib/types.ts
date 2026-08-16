/** Mirrors the DTOs in FpaiConnect.Application.Dtos. Enums travel as names. */

export type Role = 'SuperAdmin' | 'DepartmentHead' | 'Staff' | 'ExternalAccountant'
export type UserStatus = 'Invited' | 'Active' | 'Suspended' | 'PendingApproval' | 'Rejected'

export type WelfareCategory =
  | 'Medical' | 'Contract' | 'Salary' | 'MentalHealth' | 'Travel' | 'Accommodation'
export type WelfareStatus =
  | 'New' | 'UnderReview' | 'Assigned' | 'InProgress' | 'Resolved' | 'Closed'
export type CasePriority = 'Low' | 'Medium' | 'High' | 'Critical'

export type LegalCaseType = 'FifaDrc' | 'Cas' | 'Psc' | 'Arbitration'
export type LegalStatus =
  | 'Registered' | 'DocumentsPending' | 'Filed' | 'HearingScheduled' | 'DecisionReceived' | 'Closed'
export type LegalOutcome = 'Pending' | 'Won' | 'Lost' | 'Settled' | 'Withdrawn'

export type VoucherStatus = 'Draft' | 'Pending' | 'Approved' | 'Rejected' | 'Reconciled' | 'Closed'
export type ExpenseStatus =
  | 'Created' | 'InvoiceAttached' | 'PendingApproval' | 'AccountantReview'
  | 'Reconciled' | 'Closed' | 'Rejected'
export type InvoiceStatus = 'Received' | 'Verified' | 'Paid' | 'Disputed'
export type QueryStatus = 'Open' | 'Answered' | 'Resolved'

export type MeetingType = 'Board' | 'GeneralBody' | 'Committee' | 'Emergency'
export type MeetingStatus = 'Scheduled' | 'InProgress' | 'Completed' | 'Cancelled'
export type AttendeeStatus = 'Invited' | 'Accepted' | 'Declined' | 'Attended' | 'Absent'
export type MotionStatus = 'Draft' | 'Open' | 'Passed' | 'Failed' | 'Withdrawn'
export type VoteChoice = 'For' | 'Against' | 'Abstain'

export type EventType = 'Workshop' | 'Camp' | 'Outreach' | 'Ceremony' | 'Tournament'
export type EventStatus = 'Planned' | 'Dispatched' | 'Ongoing' | 'Completed' | 'Cancelled'

export type DocumentCategory =
  | 'Contract' | 'Legal' | 'Medical' | 'Financial' | 'Policy' | 'Minutes' | 'Identity' | 'Other'

export type WorkTaskStatus = 'Todo' | 'InProgress' | 'Blocked' | 'Done' | 'Cancelled'
export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'
export type PlayerStatus = 'Active' | 'Retired' | 'Injured' | 'FreeAgent'

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
}

export interface Lookup { id: string; label: string; sub?: string }

export interface UserPreferences {
  themeMode: 'System' | 'Light' | 'Dark'
  colorScheme: string
  fontChoice: string
}

export interface CurrentUser {
  id: string
  fullName: string
  email: string
  jobTitle?: string
  departmentId?: string
  departmentName?: string
  departmentCode?: string
  roles: Role[]
  status: UserStatus
  preferences: UserPreferences
}

/** Returned by registration, and by a sign-in on an account that is not yet approved. */
export interface RegistrationResult {
  status: 'PendingApproval' | 'Rejected' | 'Suspended' | 'Inactive'
  message: string
  email: string
}

export interface PendingUser {
  id: string
  fullName: string
  email: string
  jobTitle?: string
  registrationNote?: string
  signedUpWithGoogle: boolean
  createdAt: string
  status: UserStatus
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresAt: string
  user: CurrentUser
}

export interface Department {
  id: string; code: string; name: string; description?: string; userCount: number
}

export interface Club {
  id: string; name: string; city?: string; league?: string; playerCount: number
}

export interface Player {
  id: string; membershipId: string; fullName: string; dateOfBirth?: string
  position?: string; nationality: string; currentClubId?: string; currentClubName?: string
  jerseyNumber?: number; contactEmail?: string; contactPhone?: string
  status: PlayerStatus; welfareCaseCount: number; legalCaseCount: number
}

export interface Vendor {
  id: string; name: string; gstNumber?: string; contactEmail?: string
  contactPhone?: string; bankAccount?: string; voucherCount: number
}

export interface WelfareCaseListItem {
  id: string; caseNumber: string; title: string; playerId: string; playerName: string
  category: WelfareCategory; priority: CasePriority; status: WelfareStatus
  assignedOfficerId?: string; assignedOfficerName?: string; isDispute: boolean
  openedAt: string; resolvedAt?: string
}

export interface CaseNote {
  id: string; note: string; statusAtNote?: string; authorName?: string; createdAt: string
}

export interface WelfareCaseDetail extends WelfareCaseListItem {
  description?: string; resolution?: string; playerClub?: string
  departmentId: string; departmentName: string; closedAt?: string
  notes: CaseNote[]; documents: DocumentItem[]; allowedTransitions: WelfareStatus[]
}

export interface LegalCaseListItem {
  id: string; caseNumber: string; title: string; playerId: string; playerName: string
  opposingClubName?: string; type: LegalCaseType; status: LegalStatus; outcome: LegalOutcome
  priority: CasePriority; lawyerName?: string; claimAmount?: number; currency: string
  filedAt: string; hearingDate?: string
}

export interface LegalEvent {
  id: string; title: string; detail?: string; occurredAt: string
  statusAtEvent?: string; authorName?: string
}

export interface LegalCaseDetail extends LegalCaseListItem {
  description?: string; opposingClubId?: string; departmentId: string; departmentName: string
  lawyerFirm?: string; assignedCounselId?: string; assignedCounselName?: string
  awardedAmount?: number; decisionDate?: string; closedAt?: string
  events: LegalEvent[]; documents: DocumentItem[]; allowedTransitions: LegalStatus[]
}

export interface VoucherListItem {
  id: string; voucherNumber: string; vendorId: string; vendorName: string
  departmentId: string; departmentName: string; amount: number; taxAmount: number
  totalAmount: number; currency: string; status: VoucherStatus
  voucherDate: string; openQueryCount: number
}

export interface AccountantQuery {
  id: string; voucherId?: string; voucherNumber?: string; expenseId?: string; expenseNumber?: string
  question: string; response?: string; status: QueryStatus
  raisedByName?: string; answeredByName?: string; createdAt: string; answeredAt?: string
}

export interface VoucherDetail extends VoucherListItem {
  description?: string; approvedByName?: string; approvedAt?: string; rejectionReason?: string
  reconciledByName?: string; reconciledAt?: string
  queries: AccountantQuery[]; documents: DocumentItem[]; allowedTransitions: VoucherStatus[]
}

export interface ExpenseListItem {
  id: string; expenseNumber: string; title: string; category?: string
  departmentId: string; departmentName: string; amount: number; currency: string
  status: ExpenseStatus; incurredOn: string; submittedByName?: string; invoiceCount: number
}

export interface Invoice {
  id: string; invoiceNumber: string; vendorId?: string; vendorName?: string; expenseId?: string
  amount: number; taxAmount: number; currency: string; status: InvoiceStatus
  issuedOn: string; dueDate?: string; paidOn?: string
}

export interface ExpenseDetail extends ExpenseListItem {
  description?: string; approvedByName?: string; approvedAt?: string; rejectionReason?: string
  invoices: Invoice[]; queries: AccountantQuery[]; documents: DocumentItem[]
  allowedTransitions: ExpenseStatus[]
}

export interface MonthlyTrendPoint {
  year: number; month: number; label: string; income: number; expense: number
}
export interface DepartmentSpend {
  departmentId: string; departmentName: string; spent: number; budgeted: number
}
export interface FinanceSummary {
  monthlyIncome: number; monthlyExpense: number; pendingVouchers: number; openQueries: number
  trend: MonthlyTrendPoint[]; byDepartment: DepartmentSpend[]
}

export interface MeetingListItem {
  id: string; referenceNumber: string; title: string; type: MeetingType; status: MeetingStatus
  scheduledAt: string; durationMinutes: number; location?: string; chairName?: string
  attendeeCount: number; motionCount: number
}

export interface Attendee {
  id: string; userId: string; userName: string; departmentName?: string
  status: AttendeeStatus; isVotingMember: boolean
}

export interface Vote {
  id: string; userId: string; userName: string; choice: VoteChoice; castAt: string
}

export interface Motion {
  id: string; meetingId: string; title: string; description?: string; status: MotionStatus
  sequenceNumber: number; votingOpensAt?: string; votingClosesAt?: string
  passThreshold: number; isSecretBallot: boolean
  votesFor: number; votesAgainst: number; votesAbstain: number; eligibleVoters: number
  myVote?: VoteChoice; canVote: boolean; votes: Vote[]
}

export interface MeetingDetail extends MeetingListItem {
  videoLink?: string; agenda?: string; minutes?: string; quorumRequired: number
  departmentId: string; departmentName: string; chairId?: string
  attendees: Attendee[]; motions: Motion[]; documents: DocumentItem[]
  quorumMet: boolean; allowedTransitions: MeetingStatus[]
}

export interface EventListItem {
  id: string; referenceNumber: string; name: string; type: EventType; status: EventStatus
  startDate: string; endDate?: string; venue?: string; city?: string
  budgetAmount: number; actualCost: number; expectedAttendees: number; actualAttendees: number
  ownerName?: string; participantCount: number
}

export interface EventParticipant {
  id: string; playerId: string; playerName: string; clubName?: string
  status: AttendeeStatus; notes?: string
}

export interface EventDetail extends EventListItem {
  description?: string; departmentId: string; departmentName: string; ownerId?: string
  participants: EventParticipant[]; documents: DocumentItem[]; allowedTransitions: EventStatus[]
}

export interface DocumentItem {
  id: string; title: string; fileName: string; contentType: string; sizeBytes: number
  category: DocumentCategory; isConfidential: boolean; version: number
  departmentId: string; departmentName?: string; uploadedByName?: string
  createdAt: string; linkedTo?: string; linkedId?: string
}

export interface WorkTask {
  id: string; referenceNumber: string; title: string; description?: string
  status: WorkTaskStatus; priority: CasePriority; departmentId: string; departmentName: string
  assigneeId?: string; assigneeName?: string; dueDate?: string; completedAt?: string
  createdAt: string; isOverdue: boolean
  relatedEntityType?: string; relatedEntityId?: string; allowedTransitions: WorkTaskStatus[]
}

export interface ApprovalRequestItem {
  id: string; referenceNumber: string; title: string; description?: string
  status: ApprovalStatus; entityType: string; entityId: string; amount?: number
  departmentId: string; departmentName: string; requestedByName?: string; decidedByName?: string
  createdAt: string; decidedAt?: string; decisionComment?: string; canDecide: boolean
}

export interface NotificationItem {
  id: string; title: string; body?: string; link?: string; isRead: boolean; createdAt: string
}

export interface UserListItem {
  id: string; fullName: string; email: string; jobTitle?: string
  departmentId?: string; departmentName?: string; roles: Role[]; status: UserStatus
  hasGoogleLinked: boolean; createdAt: string; lastLoginAt?: string
}

export interface RoleInfo { name: Role; description?: string; userCount: number }

export interface CountByLabel { label: string; count: number }
export interface ParticipationPoint { label: string; participationRate: number; motionsClosed: number }
export interface Activity {
  entityName: string; action: string; summary: string; userName?: string; timestamp: string
}

export interface Dashboard {
  activeWelfareCases: number; activeLegalMatters: number; monthlyExpense: number
  upcomingMeetings: number; pendingTasks: number; pendingApprovals: number
  welfareByStatus: CountByLabel[]; legalByType: CountByLabel[]
  financeTrend: MonthlyTrendPoint[]; votingTrend: ParticipationPoint[]
  recentActivity: Activity[]
}

export interface ReportSummary {
  title: string; description: string; rows: CountByLabel[]; total?: number
}
export interface ReportDefinition { key: string; title: string; description: string }
