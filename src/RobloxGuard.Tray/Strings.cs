namespace RobloxGuard.Tray;

/// <summary>트레이 앱의 한국어 문자열 전부. 아이에게 보이는 문구는 중립적으로.</summary>
internal static class Strings
{
    public const string AppName = "RobloxGuard";

    public const string StatusBlocked = "상태: 차단 중";
    public const string StatusAllowedFmt = "상태: 허용 중 ({0}분 남음)";
    public const string StatusDisconnected = "서비스에 연결할 수 없음";

    public const string MenuAllow = "허용하기…";
    public const string MenuBlockNow = "지금 차단하기";
    public const string MenuLog = "기록 보기…";
    public const string MenuChangePassword = "비밀번호 변경…";
    public const string MenuAbout = "정보";

    public const string PasswordPrompt = "부모 비밀번호를 입력하세요";
    public const string PasswordHint = "가급적 부모 계정으로 로그인해서 입력하세요";
    public const string PasswordHintStrong = "⚠ 지금은 일반 계정입니다. 부모 계정에서 입력하는 것이 더 안전합니다";

    public const string AllowTitle = "허용 시간 선택";
    public const string Allow30 = "30분";
    public const string Allow60 = "1시간";
    public const string Allow120 = "2시간";
    public const string AllowToday = "오늘 하루";

    public const string ErrWrongPassword = "비밀번호가 틀렸습니다";
    public const string ErrLockedFmt = "시도가 너무 많습니다. {0}분 뒤 다시 시도하세요";
    public const string ErrNotConfigured = "비밀번호가 설정되지 않았습니다. 설치 스크립트를 다시 실행하세요";
    public const string ErrDisconnected = "서비스에 연결할 수 없습니다";
    public const string ErrGeneric = "요청을 처리하지 못했습니다";

    public const string ExpiringFmt = "{0}분 뒤 로블록스가 종료됩니다";

    public const string LogTitle = "시도 기록";
    public const string LogDayFmt = "{0:M월 d일 (ddd)} 시도 {1}회";
    public const string LogShow30 = "30일 보기";
    public const string LogEmpty = "기록이 없습니다";

    public const string ChangePwTitle = "비밀번호 변경";
    public const string ChangePwOld = "현재 비밀번호";
    public const string ChangePwNew = "새 비밀번호 (4자 이상)";
    public const string ChangePwConfirm = "새 비밀번호 확인";
    public const string ChangePwMismatch = "새 비밀번호가 일치하지 않습니다";
    public const string ChangePwTooShort = "새 비밀번호는 4자 이상이어야 합니다";
    public const string ChangePwOk = "비밀번호를 변경했습니다";

    public const string About = "RobloxGuard 1.0\n부모가 풀어주기 전까지 로블록스를 차단합니다.";
    public const string Ok = "확인";
    public const string Cancel = "취소";
}
