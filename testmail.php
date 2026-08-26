<?php  
ini_set('display_errors', 1);  
ini_set('display_startup_errors', 1);  
error_reporting(E_ALL);  
  
if ($_SERVER['REQUEST_METHOD'] === 'POST') {   
    $input = json_decode(file_get_contents('php://input'), true);  

    $username = "info@vaccinationcentre.com";  
    $recipient_email = isset($input['recipient_email']) ? trim($input['recipient_email']) : '';  
    $subject = isset($input['subject']) ? $input['subject'] : 'No Subject';
    $body = isset($input['body']) ? $input['body'] : 'No Body Content';
    $isHtml = isset($input['is_html']) && $input['is_html'];

    if (!filter_var($recipient_email, FILTER_VALIDATE_EMAIL)) {
        echo json_encode([
            "status" => "error",
            "message" => "Invalid recipient email address.",
            "debug_email" => $recipient_email
        ]);
        exit;
    }

    $headers = "From: $username\r\n";
    $headers .= "Reply-To: $username\r\n";
    $headers .= $isHtml
        ? "Content-Type: text/html; charset=UTF-8\r\n"
        : "Content-Type: text/plain; charset=UTF-8\r\n";

    $success = mail($recipient_email, $subject, $body, $headers);  

    if ($success) {  
        echo json_encode(["status" => "success", "message" => "Email sent successfully!"]);  
    } else {   
        $error = error_get_last();  
        echo json_encode([  
            "status" => "error",   
            "message" => "Failed to send email. Please check your server's mail configuration.",   
            "error_details" => $error ? $error['message'] : "No additional error details available."  
        ]);  
    }  
} else {   
    echo json_encode(["status" => "error", "message" => "Invalid request method."]);  
}  
?>  