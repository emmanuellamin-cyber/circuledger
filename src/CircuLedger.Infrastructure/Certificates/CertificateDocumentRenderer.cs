using CircuLedger.Application.Common.Interfaces;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CircuLedger.Infrastructure.Certificates;

public sealed class CertificateDocumentRenderer : ICertificateDocumentRenderer
{
    private const string Green = "#1B5E20";

    public byte[] RenderQrPng(string url) => PngByteQRCodeHelper.GetQRCode(url, QRCodeGenerator.ECCLevel.Q, 10);

    public byte[] RenderPdf(CertificateDocumentModel m)
    {
        var qr = RenderQrPng(m.VerificationUrl);

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(10.5f));

            page.Header().Column(col =>
            {
                col.Item().Text("Smart Youth Mobile Circularity Hub").FontSize(12).FontColor(Green).SemiBold();
                col.Item().Text("Njala University Â· UNDP Ã— Samsung Youth Mobile Circularity Innovation Challenge").FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(14).Text("Certificate of Data Sanitization").FontSize(22).Bold();
                col.Item().Text($"Certificate No. {m.CertificateNumber}").FontSize(13).SemiBold().FontColor(Green);
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Green);
            });

            page.Content().PaddingVertical(16).Column(col =>
            {
                col.Spacing(12);
                col.Item().Text(
                    "This certifies that all user data on the device below was erased using the method recorded here, " +
                    "and that the device was recorded on the CircuLedger tamper-evident traceability ledger.");

                col.Item().Row(row =>
                {
                    row.RelativeItem().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(150);
                            c.RelativeColumn();
                        });
                        void Line(string label, string value)
                        {
                            table.Cell().PaddingVertical(3).Text(label).SemiBold();
                            table.Cell().PaddingVertical(3).Text(value);
                        }
                        Line("Tracking ID", m.TrackingId);
                        Line("Device", $"{m.Brand} {m.Model}".Trim());
                        Line("IMEI", m.MaskedImei);
                        Line("Sanitization method", m.Method);
                        Line("Overwrite passes", m.Passes.ToString());
                        Line("Started (UTC)", m.SanitizationStartedAt.ToString("yyyy-MM-dd HH:mm"));
                        Line("Completed (UTC)", m.SanitizationCompletedAt.ToString("yyyy-MM-dd HH:mm"));
                        Line("Technician", m.TechnicianName);
                        Line("Issued (UTC)", m.IssuedAt.ToString("yyyy-MM-dd HH:mm"));
                    });
                    row.ConstantItem(150).AlignRight().Column(q =>
                    {
                        q.Item().Width(140).Image(qr);
                        q.Item().AlignCenter().Text("Scan to verify").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });

                col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(v =>
                {
                    v.Spacing(3);
                    v.Item().Text("Verification").SemiBold();
                    v.Item().Text(m.VerificationUrl).FontSize(9).FontColor(Colors.Blue.Darken2);
                    v.Item().Text($"SHA-256 payload hash: {m.PayloadHash}").FontSize(8);
                    v.Item().Text($"Signature: {m.SignatureAlgorithm}, key ID {m.SigningKeyId}").FontSize(8);
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Contains no personal data. Validity can be confirmed at any time via the URL or QR code. ").FontSize(8).FontColor(Colors.Grey.Darken1);
                t.CurrentPageNumber().FontSize(8);
            });
        })).GeneratePdf();
    }
}
